// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Outcome of <see cref="IWorkloadResolver.ResolveAsync"/>. Discriminated
/// union (sealed-record hierarchy) so callers pattern-match exhaustively on
/// the three outcomes instead of inspecting nullable fields on a single
/// record.
/// </summary>
/// <param name="Message">Human-readable summary suitable for surfacing to the user (resolution rationale or actionable hint).</param>
internal abstract record WorkloadResolution(string Message)
{
    /// <summary>Exactly one workload owns the directory.</summary>
    /// <param name="Workload">The chosen workload.</param>
    /// <param name="Message">Why it won (e.g. "Selected by --stack 'python'.").</param>
    public sealed record Resolved(WorkloadInfo Workload, string Message)
        : WorkloadResolution(Message);

    /// <summary>More than one workload claims the directory; the user must disambiguate.</summary>
    /// <param name="Candidates">Workloads that claimed the directory, with per-detector reasons.</param>
    /// <param name="Message">Pre-rendered prose listing the conflict and a hint.</param>
    public sealed record Ambiguous(IReadOnlyList<ResolutionCandidate> Candidates, string Message)
        : WorkloadResolution(Message);

    /// <summary>No installed workload claims the directory.</summary>
    /// <param name="Message">Actionable hint (typically a <c>func workload install</c> suggestion).</param>
    public sealed record None(string Message) : WorkloadResolution(Message);
}
