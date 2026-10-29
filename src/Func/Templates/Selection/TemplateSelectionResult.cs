// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;

namespace Azure.Functions.Cli.Templates.Selection;

internal enum TemplateSelectionStatus
{
    Selected,
    NotFound,
    WrongType,
    AmbiguousGroup,
    UnsatisfiedLanguage,
    InvalidArguments,
    Ambiguous,
}

/// <summary>
/// Retains deterministic candidates and parse diagnostics; selection does not establish project compatibility.
/// </summary>
internal sealed record TemplateSelectionResult(
    TemplateSelectionStatus Status,
    ImmutableArray<TemplateDescriptor> Candidates,
    ImmutableArray<TemplateArgumentParseResult> ParsedCandidates,
    ImmutableArray<string> Diagnostics)
{
    public TemplateArgumentParseResult? Selected => Status == TemplateSelectionStatus.Selected
        ? ParsedCandidates.Single(parsed => ReferenceEquals(parsed.Template, Candidates[0]))
        : null;
}
