// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Command-scoped entry point to the template engine. Create it through <see cref="ITemplaterFactory"/> and dispose
/// it when the command is finished with templates.
/// </summary>
internal sealed class Templater(TemplateEngineSession session) : IDisposable
{
    private readonly TemplateEngineSession _session = session ?? throw new ArgumentNullException(nameof(session));

    public TemplateEngineContext Context => _session.Context;

    /// <summary>
    /// Lists the installed template inventory.
    /// </summary>
    public Task<IReadOnlyList<ITemplateInfo>> ListAsync(CancellationToken cancellationToken = default)
        => _session.PackageManager.GetTemplatesAsync(cancellationToken);

    /// <summary>
    /// Instantiates the exact selected template using Microsoft.TemplateEngine's defaults for unspecified parameters.
    /// </summary>
    public async Task<ITemplateCreationResult> CreateAsync(
        ITemplateInfo template,
        string? name,
        string? fallbackName,
        string outputPath,
        IReadOnlyDictionary<string, string?> inputParameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(inputParameters);

        TemplateCreator creator = new(_session.Settings);
        ITemplateCreationResult preview = await creator.InstantiateAsync(
            template,
            name,
            fallbackName,
            outputPath,
            inputParameters,
            forceCreation: false,
            baselineName: null,
            dryRun: true,
            cancellationToken);
        if (preview.CreationEffects?.FileChanges.Any(change => change.ChangeKind == ChangeKind.Overwrite) == true)
        {
            throw new InvalidOperationException("The template would overwrite existing files.");
        }

        return await creator.InstantiateAsync(
            template,
            name,
            fallbackName,
            outputPath,
            inputParameters,
            forceCreation: false,
            baselineName: null,
            dryRun: false,
            cancellationToken);
    }

    public void Dispose() => _session.Dispose();
}
