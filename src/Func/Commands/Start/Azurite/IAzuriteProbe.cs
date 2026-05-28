// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Probes Azurite blob, queue, and table endpoints using HTTP only and reports
/// whether the emulator is ready, not listening, or being shadowed by another
/// process on the ports.
/// </summary>
internal interface IAzuriteProbe
{
    /// <summary>
    /// Probes <paramref name="endpoints"/> in parallel.
    /// </summary>
    /// <param name="endpoints">Endpoint tuple to probe. Must not be null.</param>
    /// <param name="cancellationToken">Cancellation token for the overall probe.</param>
    public Task<AzuriteProbeResult> ProbeAsync(AzuriteEndpointTuple endpoints, CancellationToken cancellationToken);
}
