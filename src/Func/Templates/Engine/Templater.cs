// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.TemplateFiltering;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;

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

        IReadOnlyList<ITemplateInfo> templates = await _packageManager.GetTemplatesAsync(cancellationToken);
        return new([.. templates.Select(template => FuncTemplateInfo.Create(template, _settings))]);
    }

    /// <summary>
    /// Lists project templates for <paramref name="language"/> using the engine's package-manager filters.
    /// </summary>
    public async Task<TemplateResolutionResult> ListProjectTemplatesAsync(string language, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        IReadOnlyList<Microsoft.TemplateEngine.Abstractions.TemplateFiltering.ITemplateMatchInfo> matches = await _packageManager.GetTemplatesAsync(
            Microsoft.TemplateEngine.Utils.WellKnownSearchFilters.MatchesAllCriteria,
            [
                Microsoft.TemplateEngine.Utils.WellKnownSearchFilters.TypeFilter("project"),
                Microsoft.TemplateEngine.Utils.WellKnownSearchFilters.LanguageFilter(language)
            ],
            cancellationToken);
        return new([.. matches.Select(match => FuncTemplateInfo.Create(match.Info, _settings))]);
    }

    /// <summary>
    /// Instantiates <paramref name="template"/> into <paramref name="outputPath"/>.
    /// </summary>
    public async Task<ITemplateCreationResult> InstantiateAsync(
        FuncTemplateInfo template,
        string? name,
        string outputPath,
        IReadOnlyDictionary<string, string> parameters,
        bool force,
        CancellationToken cancellationToken = default,
        bool dryRun = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(parameters);

        string fallbackName = Path.GetFileName(Path.TrimEndingDirectorySeparator(outputPath));
        var inputParameters = parameters.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        TemplateCreator creator = new(_settings);
        return await creator.InstantiateAsync(
            template.Template,
            name,
            fallbackName,
            outputPath,
            inputParameters,
            forceCreation: force,
            dryRun: dryRun,
            cancellationToken: cancellationToken);
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
