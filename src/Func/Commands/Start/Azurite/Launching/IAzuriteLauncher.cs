// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Launching;

/// <summary>
/// Starts a managed Azurite process (native or Docker) per the
/// managed-Azurite design (§9.1, §9.3) and returns a handle the orchestrator
/// uses to observe and stop it.
/// </summary>
internal interface IAzuriteLauncher
{
    /// <summary>
    /// Starts Azurite based on <paramref name="request"/>.
    /// </summary>
    /// <exception cref="AzuriteLaunchException">
    /// Thrown when the launcher cannot start the underlying process (executable
    /// not found, <c>docker run</c> rejected the arguments synchronously, etc.).
    /// Once the process is running, exit handling is the caller's job via
    /// <see cref="IAzuriteProcess.WaitForExitAsync"/>.
    /// </exception>
    public Task<IAzuriteProcess> StartAsync(AzuriteLaunchRequest request, CancellationToken cancellationToken);
}
