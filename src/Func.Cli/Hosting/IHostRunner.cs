// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Responsible for locating and launching the Azure Functions host runtime.
/// </summary>
public interface IHostRunner
{
    /// <summary>
    /// Starts the Functions host with the given configuration.
    /// Blocks until the host exits. Returns the host's exit code.
    /// </summary>
    public int Start(HostConfiguration config, CancellationToken cancellationToken = default);
}
