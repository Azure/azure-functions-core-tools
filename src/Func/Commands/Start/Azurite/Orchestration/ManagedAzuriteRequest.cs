// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Orchestration;

/// <summary>
/// Inputs the managed-Azurite orchestrator needs to decide whether to start
/// Azurite for a single <c>func start</c> invocation.
/// </summary>
/// <param name="StorageConnectionString">
/// Effective <c>AzureWebJobsStorage</c> value after settings + process-env
/// merging. Null or whitespace is treated the same as "no local storage".
/// </param>
/// <param name="ProjectRoot">
/// Absolute path to the resolved project root. Passed to the executable
/// locator so it can find a project-local npm Azurite binary first.
/// </param>
/// <param name="Disabled">
/// <c>true</c> when the user passed <c>--no-azurite</c>; the orchestrator
/// short-circuits before any I/O.
/// </param>
/// <param name="StartupTimeout">
/// Maximum time to wait for a freshly launched Azurite to become ready (§9.4).
/// </param>
internal sealed record ManagedAzuriteRequest(
    string? StorageConnectionString,
    string ProjectRoot,
    bool Disabled,
    TimeSpan StartupTimeout)
{
    /// <summary>
    /// Default readiness timeout per design §9.4.
    /// </summary>
    public static TimeSpan DefaultStartupTimeout { get; } = TimeSpan.FromSeconds(30);
}
