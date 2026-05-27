// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Overall result of probing an Azurite endpoint tuple. Mirrors the table in
/// §7.6 of the managed-Azurite design.
/// </summary>
internal enum AzuriteProbeStatus
{
    /// <summary>
    /// All required endpoints returned storage-shaped responses.
    /// </summary>
    Ready,

    /// <summary>
    /// No process accepts connections on any of the required endpoints.
    /// </summary>
    NotListening,

    /// <summary>
    /// Every required endpoint responded, but none returned a storage-shaped
    /// response. A non-storage service is occupying the ports.
    /// </summary>
    PortConflict,

    /// <summary>
    /// A mix of ready, not-listening, and/or non-storage responses across the
    /// required endpoints.
    /// </summary>
    Partial,
}
