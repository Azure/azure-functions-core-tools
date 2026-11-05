// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class ResolvedTemplate(
    TemplateCatalogEntry entry,
    ITemplateInfo template,
    TemplateCreator creator)
{
    private readonly ITemplateInfo _template = template ?? throw new ArgumentNullException(nameof(template));
    private readonly TemplateCreator _creator = creator ?? throw new ArgumentNullException(nameof(creator));

    public TemplateCatalogEntry Entry { get; } = entry ?? throw new ArgumentNullException(nameof(entry));

    public IReadOnlyList<TemplateParameterDefinition> Parameters => Entry.Parameters;

    public async Task<TemplateInvocationResult> InvokeAsync(
        TemplateInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parameters = request.Parameters.ToDictionary(
            item => item.Key,
            item => (string?)item.Value,
            StringComparer.Ordinal);

        ITemplateCreationResult result = await _creator.InstantiateAsync(
            _template,
            name: null,
            fallbackName: Path.GetFileName(request.OutputPath),
            request.OutputPath,
            parameters,
            request.Force,
            baselineName: null,
            dryRun: request.Mode == TemplateInvocationMode.DryRun,
            cancellationToken);

        if (result.Status != CreationResultStatus.Success)
        {
            string message = result.Status == CreationResultStatus.DestructiveChangesDetected
                ? "Container artifacts would replace existing files. Use --force to replace them."
                : result.ErrorMessage ?? $"Template '{Entry.Identity}' could not be created ({result.Status}).";
            throw new TemplateInvocationException(message);
        }

        IReadOnlyList<TemplateFileChange> changes = result.CreationEffects?.FileChanges is { } fileChanges
            ? [.. fileChanges.Select(change => new TemplateFileChange(
                NormalizeRelativePath(change.TargetRelativePath),
                change.ChangeKind switch
                {
                    ChangeKind.Create => TemplateFileChangeKind.Create,
                    ChangeKind.Delete => TemplateFileChangeKind.Delete,
                    _ => TemplateFileChangeKind.Modify,
                }))]
            : [];
        return new(changes, result.CreationResult is not null);
    }

    private static string NormalizeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }
}
