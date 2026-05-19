// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Computed filesystem layout for installed workloads. <see cref="Home"/>
/// defaults to <c>~/.azure-functions</c> and can only be overridden by
/// explicitly setting the <see cref="HomeEnvironmentVariable"/> environment
/// variable. Other configuration sources (json files, local.settings, in-memory)
/// are intentionally not honored so the workload root stays predictable.
/// </summary>
/// <remarks>
/// Only <see cref="Home"/> is settable. Everything else is computed from it
/// and exposed through <see cref="IWorkloadPaths"/>.
/// </remarks>
internal sealed class WorkloadPathsOptions : IWorkloadPaths
{
    /// <summary>
    /// Filename of the global workload registry within <see cref="Home"/>.
    /// </summary>
    public const string WorkloadRegistryFileName = "workloads.json";

    /// <summary>
    /// Environment variable that, when explicitly set to a non-empty value,
    /// overrides the default workload home directory.
    /// </summary>
    public const string HomeEnvironmentVariable = "FUNC_CLI_WORKLOADS_HOME";

    /// <summary>
    /// Root directory the func CLI persists workloads under. Defaults to the
    /// value of <see cref="HomeEnvironmentVariable"/> when explicitly set,
    /// otherwise <c>~/.azure-functions</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Home { get; set; } = ResolveDefaultHome();

    /// <inheritdoc />
    public string WorkloadsRoot => Path.Combine(Home, "workloads");

    /// <inheritdoc />
    public string WorkloadRegistryPath => Path.Combine(Home, WorkloadRegistryFileName);

    /// <inheritdoc />
    public string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);

    /// <summary>
    /// Returns the env-var override if explicitly set to a non-empty value,
    /// otherwise the default user-profile home. Centralised so callers that
    /// run before DI (e.g. workload boot) resolve Home the same way as the
    /// options pipeline.
    /// </summary>
    internal static string ResolveDefaultHome()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable(HomeEnvironmentVariable);
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName);
    }
}
