// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Packages;

/// <summary>
/// Restores the shared hive when a managed mutation or its verification fails.
/// </summary>
internal class TemplateHiveTransaction(IFileSystem fileSystem, ITemplaterFactory factory)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ITemplaterFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>
    /// Snapshots and mutates the hive while the caller holds its exclusion lock.
    /// </summary>
    /// <exception cref="TemplatePackageException">Recovery failed; its copy is retained for manual restoration.</exception>
    /// <exception cref="IOException">Hive snapshot or mutation failed.</exception>
    /// <exception cref="UnauthorizedAccessException">Hive access was denied.</exception>
    public virtual async Task ExecuteAsync(
        TemplateEngineContext context, Func<Templater, CancellationToken, Task> mutation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(mutation);
        cancellationToken.ThrowIfCancellationRequested();
        string hive = _factory.HivePath;
        // Keep the recovery copy outside the live hive and retain it if restoration fails.
        TempDirectory recovery = _fileSystem.CreateTempDirectory(Path.GetDirectoryName(hive)!);
        string backup = Path.Combine(recovery.Path, "hive");
        bool hadHive = _fileSystem.DirectoryExists(hive);
        bool committed = false;
        bool recovered = false;
        bool snapshotReady = false;
        try
        {
            if (hadHive)
            {
                _fileSystem.CopyDirectory(hive, backup);
            }

            snapshotReady = true;
            try
            {
                using Templater templater = _factory.Create(context);
                await mutation(templater, cancellationToken);
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    try
                    {
                        if (_fileSystem.DirectoryExists(hive))
                        {
                            _fileSystem.DeleteDirectory(hive);
                        }

                        if (hadHive)
                        {
                            _fileSystem.CopyDirectory(backup, hive);
                        }

                        recovered = true;
                    }
                    catch (IOException ex)
                    {
                        throw RecoveryFailure(backup, ex);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        throw RecoveryFailure(backup, ex);
                    }
                }
            }
        }
        finally
        {
            if (committed || recovered || !snapshotReady)
            {
                recovery.Dispose();
            }
        }
    }

    private static TemplatePackageException RecoveryFailure(string backup, Exception exception)
        => new($"Hive recovery failed. The previous hive is preserved at '{backup}'. Restore it before retrying.", exception);
}
