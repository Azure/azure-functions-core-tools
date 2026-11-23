// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point for package lifecycle and discovery in the func template engine.
/// </summary>
internal class Templater : IDisposable
{
    private readonly EngineEnvironmentSettings _settings;
    private readonly TemplatePackageManager _packageManager;
    private bool _disposed;

    public Templater(TemplateEngineContext context, string settingsLocation, IEnumerable<ITemplateComponent> components)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsLocation);
        ArgumentNullException.ThrowIfNull(components);

        Context = context;
        _settings = new(new FuncTemplateEngineHost(context, components), settingsLocation: settingsLocation);
        _packageManager = new(_settings);
    }

    public TemplateEngineContext Context { get; }

    internal IEngineEnvironmentSettings Settings
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _settings;
        }
    }

    /// <summary>
    /// Lists the templates installed in the func template engine.
    /// </summary>
    public async Task<TemplateResolutionResult> ListAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IReadOnlyList<ITemplateInfo> templates = await _packageManager.GetTemplatesAsync(cancellationToken);
        return new([.. templates.Select(template => FuncTemplateInfo.Create(template, _settings))]);
    }

    internal Task<IReadOnlyList<IManagedTemplatePackage>> GetManagedPackagesAsync(CancellationToken cancellationToken)
        => _packageManager.GetManagedTemplatePackagesAsync(true, cancellationToken);

    internal virtual Task<IEnumerable<ITemplateInfo>> GetTemplatesAsync(ITemplatePackage package, CancellationToken cancellationToken)
        => _packageManager.GetTemplatesAsync(package, cancellationToken);

    internal virtual async Task<InstallResult> InstallAsync(InstallRequest request, CancellationToken cancellationToken)
    {
        IManagedTemplatePackageProvider provider = _packageManager.GetBuiltInManagedProvider(InstallationScope.Global);
        IReadOnlyList<InstallResult> results = await provider.InstallAsync([request], cancellationToken);
        return results.Single();
    }

    internal async Task<UninstallResult> UninstallAsync(IManagedTemplatePackage package, CancellationToken cancellationToken)
    {
        IReadOnlyList<UninstallResult> results = await package.ManagedProvider.UninstallAsync([package], cancellationToken);
        return results.Single();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _packageManager.Dispose();
        _settings.Dispose();
    }
}
