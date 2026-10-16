// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Carries canonical values, effective aliases and distinct invalid-input, authoring and unresolved-required diagnostics.
/// </summary>
internal sealed record TemplateCandidateParseResult(
    string TemplateIdentity,
    IReadOnlyDictionary<string, string?> ValuesByCanonicalName,
    IReadOnlyList<TemplateArgumentParameter> Parameters,
    IReadOnlyList<TemplateArgumentDiagnostic> InvalidInputDiagnostics,
    IReadOnlyList<string> DefinitionDiagnostics,
    IReadOnlyList<TemplateArgumentParameter> MissingRequiredParameters)
{
    public bool IsArgumentCompatible => InvalidInputDiagnostics.Count == 0 && DefinitionDiagnostics.Count == 0;

    /// <summary>
    /// Gets whether this projected schema is complete. Native preview still performs authoritative engine validation.
    /// </summary>
    public bool IsComplete => IsArgumentCompatible && MissingRequiredParameters.Count == 0;
}

/// <summary>
/// Pairs a canonical definition with the aliases used consistently by parsing, help and required-value prompting.
/// </summary>
internal sealed record TemplateArgumentParameter(TemplateParameterDefinition Definition, string LongAlias, string? ShortAlias);

/// <summary>
/// Identifies invalid input using zero-based indices in the original template tokens, or no indices for completion values.
/// </summary>
internal sealed record TemplateArgumentDiagnostic(string Message, IReadOnlyList<int> TokenIndices);
