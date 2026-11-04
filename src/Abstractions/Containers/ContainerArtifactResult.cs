// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Containers;

/// <summary>
/// Contains the complete artifact set a workload wants core to write.
/// </summary>
public sealed record ContainerArtifactResult
{
    public ContainerArtifactResult(IReadOnlyList<ContainerArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        if (artifacts.Any(artifact => artifact is null))
        {
            throw new ArgumentException("Artifact entries cannot be null.", nameof(artifacts));
        }

        Artifacts = [.. artifacts];
    }

    /// <summary>
    /// Gets the complete set of artifacts core should write.
    /// </summary>
    public IReadOnlyList<ContainerArtifact> Artifacts { get; }
}
