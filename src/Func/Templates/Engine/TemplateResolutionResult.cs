// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateResolutionStatus
{
    Selected,
    NotFound,
    Ambiguous,
    Failed,
}

internal sealed record TemplateResolutionDiagnostic(string Code, string Message, string? TemplateIdentity = null);

internal sealed class TemplateResolutionResult
{
    public TemplateResolutionResult(
        TemplateResolutionStatus status,
        ResolvedTemplate? selectedTemplate,
        IEnumerable<TemplateCatalogEntry> candidates,
        IEnumerable<TemplateResolutionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(diagnostics);
        Status = status;
        SelectedTemplate = selectedTemplate;
        Candidates = Array.AsReadOnly<TemplateCatalogEntry>([.. candidates]);
        Diagnostics = Array.AsReadOnly<TemplateResolutionDiagnostic>([.. diagnostics]);
    }

    public TemplateResolutionStatus Status { get; }

    public ResolvedTemplate? SelectedTemplate { get; }

    public IReadOnlyList<TemplateCatalogEntry> Candidates { get; }

    public IReadOnlyList<TemplateResolutionDiagnostic> Diagnostics { get; }
}

/// <summary>
/// Represents a candidate selected by a templater session.
/// </summary>
/// <remarks>
/// Selection does not establish scan completeness, metadata freshness, or asset integrity. Invocation must validate
/// the same source and remain within a separately qualified rendering path.
/// </remarks>
internal sealed class ResolvedTemplate
{
    internal ResolvedTemplate(TemplateCatalogEntry descriptor)
        => Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));

    internal TemplateCatalogEntry Descriptor { get; }
}
