// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Probes for a usable Docker installation by running <c>docker --version</c>
/// and <c>docker info</c> per §8.4 of the managed-Azurite design.
/// </summary>
internal interface IDockerAvailabilityProbe
{
    public Task<DockerAvailability> ProbeAsync(CancellationToken cancellationToken);
}
