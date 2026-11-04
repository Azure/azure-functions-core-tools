// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Containers;

/// <summary>
/// Generates runtime-specific container artifacts for projects recognized by a workload.
/// </summary>
public interface IContainerArtifactProvider
{
    /// <summary>
    /// Returns whether this provider owns the requested capability for the resolved project.
    /// </summary>
    public bool Supports(FunctionsProject project, ContainerArtifactCapability capability);

    /// <summary>
    /// Generates the complete artifact set without writing it to disk.
    /// </summary>
    public Task<ContainerArtifactResult> GenerateAsync(ContainerArtifactRequest request, CancellationToken cancellationToken);
}
