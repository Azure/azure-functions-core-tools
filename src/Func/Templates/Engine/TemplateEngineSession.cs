// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Owns the template engine host, environment and package manager for one command.
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
    private (IReadOnlyList<ITemplatePackage> Packages, IReadOnlyList<ITemplateInfo> Templates)? _inventory;
    private bool _disposed;

    public TemplateEngineSession(TemplateEngineContext context, string settingsLocation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);

        Context = context;
        _settings = new(new FuncTemplateEngineHost(context), settingsLocation: settingsLocation);
        _packageManager = new(_settings);
    }

    public TemplateEngineContext Context { get; }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public IEngineEnvironmentSettings Settings
    {
        get
        {
            ThrowIfDisposed();
            return _settings;
        }
    }

    public TemplatePackageManager PackageManager
    {
        get
        {
            ThrowIfDisposed();
            return _packageManager;
        }
    }

    public async Task<(IReadOnlyList<ITemplatePackage> Packages, IReadOnlyList<ITemplateInfo> Templates)> GetInventoryAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (_inventory is { } inventory)
        {
            return inventory;
        }

        IReadOnlyList<ITemplatePackage> packages = await _packageManager.GetTemplatePackagesAsync(force: false, cancellationToken);
        IReadOnlyList<ITemplateInfo> templates = await _packageManager.GetTemplatesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        packages = Array.AsReadOnly(packages.ToArray());
        templates = Array.AsReadOnly(templates.ToArray());
        _inventory = (packages, templates);
        return _inventory.Value;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _inventory = null;
        _packageManager.Dispose();
        _settings.Dispose();
    }
}
