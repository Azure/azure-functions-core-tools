// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Discovery;

/// <summary>
/// Reads the per-package <see cref="WorkloadMetadata"/> manifest
/// (<c>workload.json</c>) from a workload's package root. Runs at install
/// time so the load path can rely on a pre-recorded entry point in the
/// global registry rather than re-reading the package on every CLI
/// invocation.
/// </summary>
internal interface IWorkloadMetadataReader
{
    /// <summary>
    /// Reads <c>workload.json</c> from <paramref name="packageDirectory"/>
    /// and deserializes it into a <see cref="WorkloadMetadata"/>.
    /// </summary>
    /// <param name="packageDirectory">
    /// Absolute path to the workload's package root (the directory the
    /// install pipeline lays the package contents into).
    /// </param>
    /// <exception cref="InvalidWorkloadException">
    /// <c>workload.json</c> is missing, unreadable, malformed, or omits a
    /// required property.
    /// </exception>
    public WorkloadMetadata Read(string packageDirectory);
}
