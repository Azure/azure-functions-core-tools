// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions.TemplateFiltering;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Holds the template candidates discovered for a command and, when the engine filtered them, the engine's native
/// match details for each candidate.
/// </summary>
internal sealed class TemplateResolutionResult
{
    /// <summary>
    /// Creates a result for unfiltered candidates, which carry no match details.
    /// </summary>
    public TemplateResolutionResult(IReadOnlyList<FuncTemplateInfo> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);

        Templates = templates;
        Matches = new Dictionary<FuncTemplateInfo, ITemplateMatchInfo>(ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// Creates a result for candidates the engine filtered, each paired with its native match details.
    /// </summary>
    public TemplateResolutionResult(IReadOnlyList<(FuncTemplateInfo Template, ITemplateMatchInfo Match)> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        Templates = [.. candidates.Select(candidate => candidate.Template)];
        Matches = candidates
            .ToDictionary<(FuncTemplateInfo Template, ITemplateMatchInfo Match), FuncTemplateInfo, ITemplateMatchInfo>(
                candidate => candidate.Template,
                candidate => candidate.Match,
                ReferenceEqualityComparer.Instance);
    }

    /// <summary>
    /// The candidates, in the order the engine returned them. The caller selects among them.
    /// </summary>
    public IReadOnlyList<FuncTemplateInfo> Templates { get; }

    /// <summary>
    /// The engine's native match details for each candidate in <see cref="Templates"/>, keyed by the candidate
    /// instance; empty when the candidates were not filtered.
    /// </summary>
    public IReadOnlyDictionary<FuncTemplateInfo, ITemplateMatchInfo> Matches { get; }
}
