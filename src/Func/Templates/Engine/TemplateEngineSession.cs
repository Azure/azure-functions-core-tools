// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Owns the Template Engine host, environment and package manager for one command.
/// </summary>
/// <remarks>
/// Engine components capture the environment when they are created, so every phase of a command shares this
/// session instead of building a new environment. Once disposed, the session refuses further use rather than
/// letting callers create a replacement environment without the command context.
/// </remarks>
internal sealed class TemplateEngineSession : IDisposable
{
    private readonly EngineEnvironmentSettings _settings;
    private readonly TemplatePackageManager _packageManager;
    private readonly PrivateTemplateFileSystem? _privateFileSystem;
    private bool _disposed;

    public TemplateEngineSession(TemplateEngineContext context, string settingsLocation, PrivateTemplateFileSystem? privateFileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);

        Context = context;
        _privateFileSystem = privateFileSystem;
        _settings = new(new FuncTemplateEngineHost(context, privateFileSystem), settingsLocation: settingsLocation);
        _packageManager = new(_settings);
    }

    public TemplateEngineContext Context { get; }

    internal PrivateTemplateFileSystem? PrivateFileSystem
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _privateFileSystem;
        }
    }

    public IEngineEnvironmentSettings Settings
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _settings;
        }
    }

    public TemplatePackageManager PackageManager
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _packageManager;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _packageManager.Dispose();
        }
        finally
        {
            try
            {
                _settings.Dispose();
            }
            finally
            {
                _privateFileSystem?.Dispose();
            }
        }
    }
}
