// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Outcome of <see cref="IWorkloadResolver.ResolveAsync"/>: either one
/// workload was chosen, or none was (zero claimed or several did, both
/// represented as <see cref="None"/> with an actionable message).
/// </summary>
/// <param name="Message">Human-readable summary suitable for surfacing to the user.</param>
internal abstract record WorkloadResolution(string Message)
{
    public sealed record Resolved(WorkloadInfo Workload, string Message)
        : WorkloadResolution(Message);

    public sealed record None(string Message) : WorkloadResolution(Message);
}
