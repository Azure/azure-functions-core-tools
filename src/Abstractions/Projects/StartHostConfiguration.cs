// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Stack-specific host run adjustments a workload derives from the parsed
/// <c>func start</c>/<c>func run</c> options (see <see cref="IStartHostOptionContributor.Configure"/>).
/// Applied by the CLI only when the resolved project's stack matches the contributing workload.
/// </summary>
public sealed record StartHostConfiguration
{
    /// <summary>
    /// A configuration that changes nothing about the host run.
    /// </summary>
    public static StartHostConfiguration Empty { get; } = new();

    /// <summary>
    /// Environment variables to inject into the host process. Merged over other sources, so a
    /// value here wins. Empty when the workload's options are not enabled.
    /// </summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// When set, the CLI mirrors the host process's raw (JSON) standard output to this file path.
    /// <c>null</c> disables file capture.
    /// </summary>
    public string? JsonOutputFilePath { get; init; }
}
