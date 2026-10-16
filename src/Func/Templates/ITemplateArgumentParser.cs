// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Parses untouched template tokens against one candidate's projected schema without changing the command graph.
/// </summary>
internal interface ITemplateArgumentParser
{
    public TemplateCandidateParseResult Parse(TemplateArgumentParseRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Supplies a candidate schema and the command's complete reserved aliases, with optional canonical prompt answers.
/// </summary>
/// <remarks>
/// Parse again with the original tokens and completed values after prompting. The host's name option stays separate
/// and is passed through <see cref="TemplateInvocationRequest.Name"/>, not through the parameter dictionary.
/// </remarks>
internal sealed record TemplateArgumentParseRequest(
    string TemplateIdentity,
    IReadOnlyList<TemplateParameterDefinition> Parameters,
    IReadOnlySet<string> ReservedAliases,
    IReadOnlyList<string> RawTemplateTokens,
    IReadOnlyDictionary<string, string?>? CompletedValuesByCanonicalName = null);
