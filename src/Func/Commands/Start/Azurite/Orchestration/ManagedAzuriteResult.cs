// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite.Launching;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;

/// <summary>
/// Discriminated outcome of <see cref="IManagedAzuriteOrchestrator.EnsureReadyAsync"/>.
/// The four shapes mirror the branches in §8.1: do nothing, reuse an
/// existing instance, hand back a process we started, or fail fast with a
/// user-facing message.
/// </summary>
internal abstract record ManagedAzuriteResult
{
    private ManagedAzuriteResult()
    {
    }

    /// <summary>
    /// The CLI must not manage Azurite for this invocation. Either
    /// <c>--no-azurite</c> was set or <c>AzureWebJobsStorage</c> does not
    /// reference a local emulator.
    /// </summary>
    public sealed record Disabled(string Reason) : ManagedAzuriteResult;

    /// <summary>
    /// Azurite was already responding on the configured endpoints. The CLI
    /// reused it and does not own its lifetime.
    /// </summary>
    public sealed record UserManaged(AzuriteEndpointTuple Endpoints, string Reason) : ManagedAzuriteResult;

    /// <summary>
    /// The CLI launched Azurite. <paramref name="Process"/> is the running
    /// handle; the caller is responsible for disposing it when the host run
    /// ends.
    /// </summary>
    public sealed record Started(
        IAzuriteProcess Process,
        AzuriteLaunchMode Mode,
        AzuriteEndpointTuple Endpoints) : ManagedAzuriteResult;

    /// <summary>
    /// Azurite cannot be made ready. <paramref name="UserMessage"/> is
    /// surfaced verbatim to the user; <paramref name="VerboseDetail"/> is
    /// captured for logs and the verbose error pane.
    /// </summary>
    public sealed record Failed(string UserMessage, string? VerboseDetail = null) : ManagedAzuriteResult;
}
