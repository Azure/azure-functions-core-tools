// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Outcome of <see cref="IWorkloadResolver.ResolveAsync"/>. Discriminated
/// union (sealed-record hierarchy) so callers pattern-match exhaustively
/// instead of inspecting nullable fields on a single record.
/// </summary>
/// <remarks>
/// Resolution is binary: either one workload owns the directory, or it
/// does not. "Multiple workloads claim this" and "no workload claims this"
/// are both <see cref="None"/> outcomes that differ only in their message,
/// because the caller's response is the same (surface the message, exit).
/// <c>--stack</c> remains the user escape hatch in the multi-claim case.
/// </remarks>
/// <param name="Message">Human-readable summary suitable for surfacing to the user (resolution rationale or actionable hint).</param>
internal abstract record WorkloadResolution(string Message)
{
    /// <summary>Exactly one workload owns the directory.</summary>
    /// <param name="Workload">The chosen workload.</param>
    /// <param name="Message">Why it won (e.g. "Selected by --stack 'python'.").</param>
    public sealed record Resolved(WorkloadInfo Workload, string Message)
        : WorkloadResolution(Message);

    /// <summary>No single workload could be chosen: either zero claimed, or several did.</summary>
    /// <param name="Message">Pre-rendered, actionable prose for the user.</param>
    public sealed record None(string Message) : WorkloadResolution(Message);
}
