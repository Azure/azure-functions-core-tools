// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Result of <see cref="IWorkloadResolver.ResolveAsync"/>. Records-with-enum
/// shape (rather than a sealed-class hierarchy) keeps the type cheap to
/// pattern-match in callers and matches the rest of the codebase (e.g.
/// <see cref="Install.WorkloadInstallResult"/>).
/// </summary>
/// <param name="Status">Discriminates between resolved / ambiguous / none.</param>
/// <param name="Resolved">The chosen workload when <paramref name="Status"/> is <see cref="WorkloadResolutionStatus.Resolved"/>; otherwise <c>null</c>.</param>
/// <param name="Candidates">Workloads that claimed the directory when <paramref name="Status"/> is <see cref="WorkloadResolutionStatus.Ambiguous"/>; empty otherwise.</param>
/// <param name="Message">Human-readable summary suitable for surfacing to the user (resolution rationale or actionable hint).</param>
internal sealed record WorkloadResolution(
    WorkloadResolutionStatus Status,
    WorkloadInfo? Resolved,
    IReadOnlyList<WorkloadInfo> Candidates,
    string Message)
{
    public static WorkloadResolution AsResolved(WorkloadInfo workload, string message)
        => new(WorkloadResolutionStatus.Resolved, workload, [], message);

    public static WorkloadResolution AsAmbiguous(IReadOnlyList<WorkloadInfo> candidates, string message)
        => new(WorkloadResolutionStatus.Ambiguous, Resolved: null, candidates, message);

    public static WorkloadResolution AsNone(string message)
        => new(WorkloadResolutionStatus.None, Resolved: null, [], message);
}
