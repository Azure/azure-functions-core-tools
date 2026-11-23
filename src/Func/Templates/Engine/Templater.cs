// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Edge.Settings;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point for discovering and instantiating templates installed in the func template engine.
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
    /// <exception cref="System.Text.Json.JsonException">An installed template's <c>func.host.json</c> is not valid.</exception>
    public async Task<TemplateResolutionResult> ListAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        IReadOnlyList<ITemplateInfo> templates = await _packageManager.GetTemplatesAsync(cancellationToken);
        return new([.. templates.Select(template => FuncTemplateInfo.Create(template, _settings))]);
    }

    /// <summary>
    /// Finds the installed project template for <paramref name="language"/>.
    /// </summary>
    /// <returns>The matching template, or <c>null</c> when none is installed.</returns>
    /// <exception cref="InvalidOperationException">More than one installed project template matches the language.</exception>
    /// <exception cref="System.Text.Json.JsonException">An installed template's <c>func.host.json</c> is not valid.</exception>
    /// <exception cref="FileNotFoundException">A declared template host configuration file is missing.</exception>
    /// <exception cref="IOException">A template source cannot be mounted or its host configuration cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to a template host configuration is denied.</exception>
    public virtual async Task<FuncTemplateInfo?> FindProjectTemplateAsync(string language, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        List<FuncTemplateInfo> matches = await FindTemplatesAsync("project", language, cancellationToken);

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple project templates match language '{language}': {string.Join(", ", matches.Select(t => t.Identity))}."),
        };
    }

    /// <summary>
    /// Finds the installed item templates for <paramref name="language"/>, optionally narrowed to the template whose
    /// identity matches <paramref name="reference"/>, or, when no identity matches, to those whose short name or name does.
    /// </summary>
    /// <returns>The matching templates; empty when none is installed.</returns>
    public async Task<IReadOnlyList<FuncTemplateInfo>> FindItemTemplatesAsync(
        string language, string? reference = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        List<FuncTemplateInfo> matches = await FindTemplatesAsync("item", language, cancellationToken);
        if (reference is null)
        {
            return matches;
        }

        List<FuncTemplateInfo> identityMatches =
            [.. matches.Where(t => string.Equals(t.Identity, reference, StringComparison.OrdinalIgnoreCase))];
        return identityMatches.Count > 0 ? identityMatches : [.. matches.Where(t => IsNamedBy(t, reference))];
    }

    /// <summary>
    /// Instantiates <paramref name="template"/> into <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="template">The template to instantiate.</param>
    /// <param name="name">The name substituted into the template, or <c>null</c> to use the output directory name.</param>
    /// <param name="outputPath">The directory to write the generated files to.</param>
    /// <param name="parameters">Template parameter values.</param>
    /// <param name="force">True to overwrite existing files instead of reporting a destructive change.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public async Task<ITemplateCreationResult> InstantiateAsync(
        FuncTemplateInfo template,
        string? name,
        string outputPath,
        IReadOnlyDictionary<string, string> parameters,
        bool force,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(parameters);

        string fallbackName = Path.GetFileName(Path.TrimEndingDirectorySeparator(outputPath));
        var inputParameters = parameters.ToDictionary(p => p.Key, p => (string?)p.Value);
        TemplateCreator creator = new(_settings);
        return await creator.InstantiateAsync(
            template.Template, name, fallbackName, outputPath, inputParameters, forceCreation: force, cancellationToken: cancellationToken);
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

    private async Task<List<FuncTemplateInfo>> FindTemplatesAsync(string type, string language, CancellationToken cancellationToken)
    {
        TemplateResolutionResult result = await ListAsync(cancellationToken);
        return [.. result.Templates.Where(t => HasTag(t, "type", type) && HasTag(t, "language", language))];
    }

    private static bool IsNamedBy(ITemplateInfo template, string reference)
        => string.Equals(template.Name, reference, StringComparison.OrdinalIgnoreCase)
            || template.ShortNameList.Contains(reference, StringComparer.OrdinalIgnoreCase);

    private static bool HasTag(ITemplateInfo template, string tagName, string value)
        => template.TagsCollection.TryGetValue(tagName, out string? actual) && string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
}
