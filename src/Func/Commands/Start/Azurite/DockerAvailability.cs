// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Result of a Docker availability probe.
/// </summary>
/// <param name="Status">High-level outcome category.</param>
/// <param name="Reason">Short, human-readable explanation suitable for verbose logging.</param>
/// <param name="Version">Version string from <c>docker --version</c> when available.</param>
internal sealed record DockerAvailability(
    DockerAvailabilityStatus Status,
    string Reason,
    string? Version = null);
