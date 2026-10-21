// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Binds an eligible selected template to invocation within its owning templater's live session.
/// </summary>
internal sealed class ResolvedTemplate
{
    private readonly ISelectedTemplateInvoker _invoker;

    internal ResolvedTemplate(TemplateCatalogEntry entry, ISelectedTemplateInvoker invoker)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        if (entry.ConstraintEvaluation.Status != TemplateConstraintEvaluationStatus.Eligible
            || entry.MetadataDiagnostic is not null || entry.ProjectConfigurationDiagnostic is not null)
        {
            throw new ArgumentException("Only a validated eligible template can be resolved.", nameof(entry));
        }

        Entry = entry;
    }

    public TemplateCatalogEntry Entry { get; }

    public IReadOnlyList<TemplateParameterDefinition> Parameters => Entry.Parameters;

    public Task<TemplateInvocationResult> InvokeAsync(TemplateInvocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OutputDirectory);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Parameters.Keys.Any(key => !Parameters.Any(parameter => string.Equals(parameter.CanonicalName, key, StringComparison.Ordinal))))
        {
            return Task.FromResult(new TemplateInvocationResult(TemplateInvocationStatus.InvalidArguments, [], "Invocation values must use declared canonical parameter names."));
        }

        request = request with { Parameters = new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal) };
        return _invoker.InvokeAsync(request, cancellationToken);
    }
}