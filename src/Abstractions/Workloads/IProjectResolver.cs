// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Decides whether a directory is a project owned by this workload.
/// Registered from <see cref="Workload.Configure"/> via
/// <see cref="FunctionsCliBuilder.RegisterProjectResolver"/>.
/// </summary>
public interface IProjectResolver
{
    /// <summary>
    /// Inspects <paramref name="workingDirectory"/> and returns whether the
    /// workload claims it. Implementers do their own pre-filtering and
    /// should return quickly when the directory clearly isn't theirs.
    /// </summary>
    public Task<EvaluationResult> EvaluateAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken);
}
