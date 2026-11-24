// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Immutable command options that control <c>func start</c> initialization.
/// </summary>
internal sealed record StartCommandOptions(
    WorkingDirectory WorkingDirectory,
    int? Port,
    IReadOnlyList<string> Cors,
    bool CorsCredentials,
    IReadOnlyList<string> Functions,
    bool NoBuild,
    bool EnableAuth,
    string? RequestedProfileName,
    string? RequestedHostVersion,
    bool Offline,
    OutputMode OutputMode,
    bool NoTui,
    string? LogFilePath,
    bool DemoMode,
    int DemoFunctionCount,
    double DemoSpeedMultiplier,
    bool DemoAutoExit,
    bool NoAzurite = false,
    IReadOnlyDictionary<IProjectStack, StartHostConfiguration>? StackHostConfigurations = null)
{
    /// <summary>
    /// Host run adjustments keyed by the owning installed stack.
    /// The entry matching the resolved project's stack is applied during host startup.
    /// </summary>
    public IReadOnlyDictionary<IProjectStack, StartHostConfiguration> StackHostConfigurations { get; init; }
        = StackHostConfigurations ?? new Dictionary<IProjectStack, StartHostConfiguration>(ReferenceEqualityComparer.Instance);
}
