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
    /// Environment variable prefix the CLI uses to defer variables that must not be set on the host
    /// process itself (e.g. DOTNET_STARTUP_HOOKS) but should be inherited by worker child processes.
    /// The host strips this prefix post-boot and re-emits the target variable. Both the CLI workload
    /// and the host must agree on this value.
    /// </summary>
    public const string DeferredWorkerEnvironmentPrefix = "FUNCTIONS_CORETOOLS_DEFER_ENV__";

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

    /// <summary>
    /// An optional message the CLI shows the user before the host starts (e.g. a note that the
    /// worker will pause until a debugger attaches). The workload owns the wording; the CLI just
    /// renders it. <c>null</c> shows nothing.
    /// </summary>
    public string? StartupNotice { get; init; }
}
