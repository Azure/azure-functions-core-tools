// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Owns live-hive admission, isolated runtime settings, and opaque transaction recovery.
/// </summary>
internal sealed class TemplateStore(CliConfigurationPathsOptions paths, IFileSystem fileSystem,
    ITemplateStoreLock storeLock, ILoggerFactory loggerFactory) : ITemplateStore
{
    private readonly CliConfigurationPathsOptions _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ITemplateStoreLock _storeLock = storeLock ?? throw new ArgumentNullException(nameof(storeLock));
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

    internal string Hive => Path.Combine(_paths.Home, "templates");

    internal string ControlDirectory => Path.Combine(_paths.Home, ".template-store");

    internal string Backup => Path.Combine(ControlDirectory, "recovery");

    internal string Journal => Path.Combine(ControlDirectory, "recovery.ready");

    public async Task<TemplateEngineSession> OpenReadAsync(TemplateEngineContext context, CancellationToken cancellationToken,
        TemplateConstraintEvaluationSnapshot? snapshot = null,
        Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        while (true)
        {
            IDisposable lease = await _storeLock.AcquireAsync(ControlDirectory, TemplateStoreAccess.Read, cancellationToken, waiting);
            TempDirectory? settings = null;
            bool transferred = false;
            bool recoveryNeeded;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                recoveryNeeded = _fileSystem.FileExists(Journal);
                if (!recoveryNeeded)
                {
                    settings = _fileSystem.CreateTempDirectory(ControlDirectory);
                    if (_fileSystem.DirectoryExists(Hive))
                    {
                        await _fileSystem.CopyDirectoryAsync(Hive, settings.Value.Path, cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    TemplateEngineSession session = new(context, settings.Value.Path, snapshot,
                        new ReadOwnership(settings.Value, lease), new TemplateStoreEngineDiagnostics(_loggerFactory));
                    transferred = true;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        session.Dispose();
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    return session;
                }
            }
            finally
            {
                if (!transferred)
                {
                    settings?.Dispose();
                    lease.Dispose();
                }
            }

            // Never upgrade a shared lease. Recheck recovery after obtaining exclusive ownership.
            using IDisposable recovery = await _storeLock.AcquireAsync(ControlDirectory, TemplateStoreAccess.Write, cancellationToken, waiting);
            await RecoverAsync(cancellationToken);
        }
    }

    public async Task<TemplateStoreWriteSession> OpenWriteAsync(TemplateEngineContext context, CancellationToken cancellationToken,
        Action<TemplateStoreWaitReason>? waiting = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        IDisposable lease = await _storeLock.AcquireAsync(ControlDirectory, TemplateStoreAccess.Write, cancellationToken, waiting);
        bool transferred = false;
        try
        {
            await RecoverAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            TemplateStoreWriteSession session = new(this, context, lease);
            transferred = true;
            return session;
        }
        finally
        {
            if (!transferred)
            {
                lease.Dispose();
            }
        }
    }

    internal TemplateEngineSession CreateWriteEngine(TemplateEngineContext context)
        => new(context, Hive, diagnostics: new TemplateStoreEngineDiagnostics(_loggerFactory));

    internal async Task<TemplateEngineSession> CreateRecoveryEngineAsync(TemplateEngineContext context)
    {
        TempDirectory settings = _fileSystem.CreateTempDirectory(ControlDirectory);
        bool transferred = false;
        try
        {
            if (_fileSystem.DirectoryExists(Hive))
            {
                await _fileSystem.CopyDirectoryAsync(Hive, settings.Path, CancellationToken.None);
            }

            TemplateEngineSession engine = new(context, settings.Path, ownership: settings,
                diagnostics: new TemplateStoreEngineDiagnostics(_loggerFactory));
            transferred = true;
            return engine;
        }
        finally
        {
            if (!transferred)
            {
                settings.Dispose();
            }
        }
    }

    internal async Task SnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_fileSystem.DirectoryExists(Backup))
        {
            _fileSystem.DeleteDirectory(Backup);
        }

        bool exists = _fileSystem.DirectoryExists(Hive);
        _fileSystem.CreateDirectory(Backup);
        if (exists)
        {
            await _fileSystem.CopyDirectoryAsync(Hive, Backup, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _fileSystem.WriteAllTextAsync(Journal, exists ? "present" : "absent", cancellationToken);
    }

    internal void Commit()
    {
        _fileSystem.DeleteFile(Journal);
        // The journal's removal is the commit point. Backup cleanup is deferred to the next writer.
    }

    internal async Task<bool> RecoverAsync(CancellationToken cancellationToken)
    {
        string? state = await _fileSystem.ReadAllTextIfExistsAsync(Journal, cancellationToken);
        if (state is null)
        {
            return false;
        }

        if (state is not ("present" or "absent") || !_fileSystem.DirectoryExists(Backup))
        {
            throw new InvalidDataException("The template-store recovery snapshot is incomplete. Preserve the store and request repair before retrying.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Once restoration starts it must finish even when the initiating command was canceled.
        if (_fileSystem.DirectoryExists(Hive))
        {
            _fileSystem.DeleteDirectory(Hive);
        }

        if (state == "present")
        {
            await _fileSystem.CopyDirectoryAsync(Backup, Hive, CancellationToken.None);
        }

        _fileSystem.DeleteFile(Journal);
        return true;
    }

    private sealed class ReadOwnership(TempDirectory settings, IDisposable lease) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                settings.Dispose();
            }
            finally
            {
                lease.Dispose();
            }
        }
    }
}
