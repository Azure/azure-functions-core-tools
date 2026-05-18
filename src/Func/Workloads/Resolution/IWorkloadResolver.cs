// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Resolution;

/// <summary>
/// Decides which installed workload owns a directory. If a stack pin is
/// present in <see cref="StackOptions"/>, looks it up by alias and returns
/// the match. Otherwise iterates registered
/// <see cref="IProjectResolver"/>s and returns the unique claimant.
/// </summary>
internal interface IWorkloadResolver
{
    /// <summary>
    /// Never throws on resolution failure; callers pattern-match the result.
    /// </summary>
    public Task<WorkloadResolution> ResolveAsync(DirectoryInfo directory, CancellationToken cancellationToken);
}
