// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Locates an Azurite executable installed on the host, preferring the
/// project-local npm binary over a <c>PATH</c> match. Implementations follow
/// the resolution order described in §8.2 of the managed-Azurite design.
/// </summary>
internal interface IAzuriteExecutableLocator
{
    /// <summary>
    /// Searches for an Azurite executable.
    /// </summary>
    /// <param name="projectRoot">Absolute path to the user's function app project root.</param>
    /// <param name="cancellationToken">Caller cancellation token.</param>
    /// <returns>The discovered executable, or <c>null</c> when none was found.</returns>
    public Task<AzuriteExecutable?> FindAsync(string projectRoot, CancellationToken cancellationToken);
}
