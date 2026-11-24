// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.TestHelpers.Templates;

/// <summary>
/// Hosts the func template engine over a templates directory in an isolated temporary home, for template tests.
/// </summary>
/// <remarks>
/// Templates are surfaced through the same workload template provider used in production, so they are discovered
/// without being installed into the engine.
/// </remarks>
public sealed class TemplateTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly Templater _templater;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateTestHost"/> class.
    /// </summary>
    /// <param name="templatesDirectory">The directory containing the template folders to expose.</param>
    public TemplateTestHost(string templatesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesDirectory);
        if (!Directory.Exists(templatesDirectory))
        {
            throw new DirectoryNotFoundException($"Templates directory '{templatesDirectory}' does not exist.");
        }

        Root = Path.Combine(Path.GetTempPath(), "func-template-tests-" + Guid.NewGuid().ToString("N"));
        OutputDirectory = Path.Combine(Root, "output");
        Directory.CreateDirectory(OutputDirectory);

        WorkloadTemplateRoot root = new(
            Path.GetFullPath(templatesDirectory), "Azure.Functions.Cli.TestTemplates", "1.0.0", DateTime.UtcNow);

        _services = new ServiceCollection()
            .AddSingleton(new CliConfigurationPathsOptions(Path.Combine(Root, "home")))
            .AddSingleton<IWorkloadTemplateSource>(new FixedTemplateSource(root))
            .AddTemplating()
            .BuildServiceProvider();

        TemplateEngineContext context = new(WorkingDirectory.FromExplicit(OutputDirectory));
        _templater = _services.GetRequiredService<ITemplaterFactory>().Create(context);
    }

    /// <summary>
    /// Gets the temporary directory that holds the engine settings and generated output.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Gets the default directory templates are generated into.
    /// </summary>
    public string OutputDirectory { get; }

    /// <summary>
    /// Gets the single template with <paramref name="shortName"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No template, or more than one, has the short name.</exception>
    public async Task<ITemplateInfo> GetTemplateAsync(string shortName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortName);

        TemplateResolutionResult result = await _templater.ListAsync(cancellationToken);
        return result.Templates.Single(template => template.ShortNameList.Contains(shortName));
    }

    /// <summary>
    /// Generates <paramref name="template"/> with the given name and parameters.
    /// </summary>
    /// <param name="template">The template to generate.</param>
    /// <param name="name">The name passed to the template, also used as the fallback name.</param>
    /// <param name="parameters">The template parameter values.</param>
    /// <param name="outputDirectory">The output directory; defaults to <see cref="OutputDirectory"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The template creation result.</returns>
    public Task<ITemplateCreationResult> InstantiateAsync(
        ITemplateInfo template,
        string name,
        IReadOnlyDictionary<string, string?>? parameters = null,
        string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        TemplateCreator creator = new(_templater.Settings);
        return creator.InstantiateAsync(
            template,
            name,
            name,
            outputDirectory ?? OutputDirectory,
            parameters ?? new Dictionary<string, string?>(),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _templater.Dispose();
        await _services.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp directory doesn't affect test results.
        }
    }

    private sealed class FixedTemplateSource(WorkloadTemplateRoot root) : IWorkloadTemplateSource
    {
        public Task<IReadOnlyList<WorkloadTemplateRoot>> GetRootsAsync(CancellationToken cancellation = default)
            => Task.FromResult<IReadOnlyList<WorkloadTemplateRoot>>([root]);
    }
}
