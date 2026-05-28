// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;

/// <summary>
/// Wires the classifier, probe, locator, Docker probe, launcher, and managed
/// paths into the single decision the <c>func start</c> pipeline needs:
/// "is Azurite ready, and if not, can I make it ready?". See §8.1.
/// </summary>
internal interface IManagedAzuriteOrchestrator
{
    /// <summary>
    /// Resolves Azurite for one <c>func start</c> invocation.
    /// </summary>
    public Task<ManagedAzuriteResult> EnsureReadyAsync(
        ManagedAzuriteRequest request,
        CancellationToken cancellationToken);
}
