// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// Best-effort resolver from a listening TCP port to the owning process and its
/// command line. Used to tell whether an existing Azurite endpoint is a managed
/// instance and which data directory it is serving.
/// </summary>
internal interface IListeningProcessInspector
{
    /// <summary>
    /// Returns every process listening on the given TCP port (on any bind
    /// address), empty when none can be determined (no listener, permission
    /// denied, missing OS tooling, or an unsupported platform). More than one
    /// can be returned on dual-stack hosts, so callers pick the relevant one.
    /// </summary>
    public Task<IReadOnlyList<ListeningProcessInfo>> GetListeningProcessesAsync(int port, CancellationToken cancellationToken);
}
