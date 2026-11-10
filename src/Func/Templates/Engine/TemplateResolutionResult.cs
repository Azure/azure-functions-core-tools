// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

internal enum TemplateResolutionStatus
{
    Selected,
    NotFound,
    Ambiguous,
    Failed,
}

internal sealed record TemplateResolutionDiagnostic(
    string Code,
    string Message,
    string? TemplateIdentity = null,
    string? PackageIdentity = null,
    Exception? Cause = null);

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
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        TemplateCatalogEntry[] candidateArray = [.. candidates];
        TemplateResolutionDiagnostic[] diagnosticArray = [.. diagnostics];
        switch (status)
        {
            case TemplateResolutionStatus.Selected when selectedTemplate is null || candidateArray.Length != 0:
                throw new ArgumentException("A selected result requires a selection and cannot contain ambiguity candidates.", nameof(selectedTemplate));
            case TemplateResolutionStatus.NotFound when selectedTemplate is not null || candidateArray.Length != 0:
                throw new ArgumentException("A not-found result cannot contain a selection or candidates.", nameof(candidates));
            case TemplateResolutionStatus.Ambiguous when selectedTemplate is not null || candidateArray.Length < 2:
                throw new ArgumentException("An ambiguous result requires multiple candidates and no selection.", nameof(candidates));
            case TemplateResolutionStatus.Failed when selectedTemplate is not null || diagnosticArray.Length == 0:
                throw new ArgumentException("A failed result requires diagnostics and cannot contain a selection.", nameof(diagnostics));
        }

        Status = status;
        SelectedTemplate = selectedTemplate;
        Candidates = Array.AsReadOnly(candidateArray);
        Diagnostics = Array.AsReadOnly(diagnosticArray);
    }

    public TemplateResolutionStatus Status { get; }

    public ResolvedTemplate? SelectedTemplate { get; }

    public IReadOnlyList<TemplateCatalogEntry> Candidates { get; }

    public IReadOnlyList<TemplateResolutionDiagnostic> Diagnostics { get; }
}

/// <summary>
/// Represents a candidate selected by a templater session.
/// </summary>
internal sealed class ResolvedTemplate
{
    private readonly TemplateEngineSession _session;
    private readonly ITemplateInfo _template;

    internal ResolvedTemplate(TemplateEngineSession session, TemplateCatalogEntry descriptor, ITemplateInfo template)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _template = template ?? throw new ArgumentNullException(nameof(template));
    }

    internal ITemplateInfo Template
    {
        get
        {
            _session.ThrowIfDisposed();
            return _template;
        }
    }

    internal TemplateCatalogEntry Descriptor { get; }
}
