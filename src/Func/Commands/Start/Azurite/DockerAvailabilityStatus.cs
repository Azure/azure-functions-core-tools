// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Outcome of probing for a usable Docker installation.
/// </summary>
internal enum DockerAvailabilityStatus
{
    /// <summary>Both <c>docker --version</c> and <c>docker info</c> succeeded.</summary>
    Available,

    /// <summary>The <c>docker</c> executable is not on the host.</summary>
    ExecutableNotFound,

    /// <summary>Docker is installed but the daemon is not reachable.</summary>
    DaemonUnavailable,

    /// <summary>A Docker command failed in an unexpected way (timeout or other error).</summary>
    CommandFailed,
}
