// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed record TemplateResolutionDiagnostic(
    string Code,
    string Message,
    string? TemplateIdentity = null,
    string? PackageIdentity = null,
    Exception? Cause = null,
    string? TemplateName = null,
    TemplateType? TemplateKind = null,
    string? TemplateSource = null);

internal sealed class TemplateResolutionResult
{
    public TemplateResolutionResult(
        TemplateCatalogEntry[] candidates,
        TemplateResolutionDiagnostic[] diagnostics)
    {
        Candidates = candidates ?? throw new ArgumentNullException(nameof(candidates));
        Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public IReadOnlyList<TemplateCatalogEntry> Candidates { get; }

    public IReadOnlyList<TemplateResolutionDiagnostic> Diagnostics { get; }
}
