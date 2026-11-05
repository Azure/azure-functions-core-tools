// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateResolutionStatus
{
    Selected,
    NotFound,
    WrongKind,
    Ineligible,
    Ambiguous,
    DuplicateIdentity,
    InvalidSelection,
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
/// Binds a catalog selection to its originating engine session.
/// </summary>
/// <remarks>
/// Selection does not establish scan completeness, metadata freshness, or asset integrity. Invocation must validate
/// the same source and remain within a separately qualified rendering path.
/// </remarks>
internal sealed class ResolvedTemplate
{
    private readonly TemplateEngineSession _owner;
    private readonly ITemplateInfo _templateInfo;

    internal ResolvedTemplate(TemplateEngineSession owner, TemplateCatalogEntry descriptor, ITemplateInfo templateInfo)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _templateInfo = templateInfo ?? throw new ArgumentNullException(nameof(templateInfo));

        if (!string.Equals(Descriptor.Identity, _templateInfo.Identity, StringComparison.Ordinal))
        {
            throw new ArgumentException("The selected template must match its catalog identity.", nameof(templateInfo));
        }
    }

    internal TemplateCatalogEntry Descriptor { get; }


    internal bool IsOwnedBy(TemplateEngineSession session)
        => ReferenceEquals(_owner, session)
            && string.Equals(Descriptor.Identity, _templateInfo.Identity, StringComparison.Ordinal);

}
