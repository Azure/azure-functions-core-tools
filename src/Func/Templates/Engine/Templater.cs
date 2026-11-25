// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplateFiltering;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Utils;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point for discovering templates installed in the func template engine.
/// </summary>
internal sealed class Templater : IDisposable
{
    private readonly EngineEnvironmentSettings _settings;
    private readonly TemplatePackageManager _packageManager;
    private bool _disposed;

    /// <summary>
    /// Initializes a templater for a command-scoped engine environment.
    /// </summary>
    /// <param name="context">The current command context.</param>
    /// <param name="settingsLocation">The engine settings directory.</param>
    /// <param name="components">The components to register with the engine.</param>
    public Templater(
        TemplateEngineContext context, string settingsLocation, IReadOnlyList<ICoreTemplateComponent> components)
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
        return new TemplateResolutionResult(
            [.. templates.Select(template => FuncTemplateInfo.Create(template, _settings))]);
    }

    /// <summary>
    /// Lists project templates for <paramref name="language"/> using the engine's package-manager filters, keeping
    /// the engine's match details for each candidate.
    /// </summary>
    public async Task<TemplateResolutionResult> ListProjectTemplatesAsync(
        string language,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        IReadOnlyList<ITemplateMatchInfo> matches = await _packageManager.GetTemplatesAsync(
            WellKnownSearchFilters.MatchesAllCriteria,
            [
                WellKnownSearchFilters.TypeFilter("project"),
                WellKnownSearchFilters.LanguageFilter(language)
            ],
            cancellationToken);
        return new TemplateResolutionResult(
            [.. matches.Select(match => (FuncTemplateInfo.Create(match.Info, _settings), match))]);
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
