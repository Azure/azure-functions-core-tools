// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Contains values keyed by canonical template parameter name, the aliases used for parsing, input errors,
/// template-definition errors, and required parameters still missing values.
/// </summary>
internal sealed record TemplateCandidateParseResult(
    string TemplateIdentity,
    IReadOnlyDictionary<string, string?> ValuesByCanonicalName,
    IReadOnlyList<TemplateArgumentParameter> Parameters,
    IReadOnlyList<TemplateArgumentDiagnostic> InvalidInputDiagnostics,
    IReadOnlyList<string> DefinitionDiagnostics,
    IReadOnlyList<TemplateArgumentParameter> MissingRequiredParameters)
{
    public IReadOnlySet<string> ExplicitParameterNames { get; init; } = FrozenSet<string>.Empty;

    public bool IsArgumentCompatible => InvalidInputDiagnostics.Count == 0 && DefinitionDiagnostics.Count == 0;

    /// <summary>
    /// Gets whether parsing succeeded and all required parameters have values.
    /// The template engine still validates them during preview.
    /// </summary>
    public bool IsComplete => IsArgumentCompatible && MissingRequiredParameters.Count == 0;
}

/// <summary>
/// Stores a template parameter definition and its aliases for parsing, help, and prompts.
/// </summary>
internal sealed record TemplateArgumentParameter(TemplateParameterDefinition Definition, string LongAlias, string? ShortAlias);

/// <summary>
/// Identifies invalid input using zero-based indices in the original template tokens, or no indices for completion values.
/// </summary>
internal sealed record TemplateArgumentDiagnostic(string Message, IReadOnlyList<int> TokenIndices);
