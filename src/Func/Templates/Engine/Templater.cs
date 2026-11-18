// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point for discovering templates installed in the func template engine.
/// </summary>
internal sealed class Templater : IDisposable
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

        // TODO: Validate raw func constraints on engine-mounted packages before GetTemplatesAsync.
        // Use the engine's package enumeration and scanner to locate declarations; cached results
        // have already collapsed identities and cannot validate the original constraint JSON.
        IReadOnlyList<ITemplateInfo> templates = await _packageManager.GetTemplatesAsync(cancellationToken);
        return new([.. templates]);
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
