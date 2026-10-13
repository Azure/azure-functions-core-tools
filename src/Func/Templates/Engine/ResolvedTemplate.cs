// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class ResolvedTemplate
{
    private readonly Func<TemplateInvocationRequest, CancellationToken, Task<TemplateInvocationResult>> _invoke;

    internal ResolvedTemplate(TemplateCatalogEntry entry, Func<TemplateInvocationRequest, CancellationToken, Task<TemplateInvocationResult>> invoke)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        if (entry.ConstraintEvaluation.Status != TemplateConstraintEvaluationStatus.Eligible || entry.MetadataDiagnostic is not null)
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
        if (request.Parameters.Keys.Any(key => !Parameters.Any(parameter => string.Equals(parameter.CanonicalName, key, StringComparison.Ordinal))))
        {
            return Task.FromResult(new TemplateInvocationResult(TemplateInvocationStatus.Failed, [], "Invocation values must use declared canonical parameter names."));
        }

        request = request with { Parameters = new Dictionary<string, string?>(request.Parameters, StringComparer.Ordinal) };
        return _invoke(request, cancellationToken);
    }
}