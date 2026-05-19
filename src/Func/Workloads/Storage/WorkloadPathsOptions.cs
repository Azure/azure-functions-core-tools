// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Computed filesystem layout for installed workloads. <see cref="Home"/>
/// defaults to <c>~/.azure-functions</c> and can only be overridden by
/// explicitly setting the <see cref="Constants.WorkloadsHomeEnvironmentVariable"/>
/// environment variable. Other configuration sources (json files,
/// local.settings, in-memory) are intentionally not honored so the workload
/// root stays predictable.
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
    /// Root directory the func CLI persists workloads under. Defaults to the
    /// value of <see cref="Constants.WorkloadsHomeEnvironmentVariable"/> when
    /// explicitly set, otherwise <c>~/.azure-functions</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Home { get; set; } = ResolveHome();

    /// <inheritdoc />
    public string WorkloadsRoot => Path.Combine(Home, "workloads");

    /// <inheritdoc />
    public string WorkloadRegistryPath => Path.Combine(Home, WorkloadRegistryFileName);

    /// <inheritdoc />
    public string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);

    /// <summary>
    /// Returns the env-var override if explicitly set to a non-whitespace
    /// value, otherwise the default user-profile home. Centralised so callers
    /// that run before DI (e.g. workload boot) resolve Home the same way as
    /// the options pipeline. The result is normalised via
    /// <see cref="Path.GetFullPath(string)"/> so downstream string comparisons
    /// are stable regardless of the input form.
    /// </summary>
    internal static string ResolveHome()
    {
        string? configured = Environment.GetEnvironmentVariable(Constants.WorkloadsHomeEnvironmentVariable);
        string home = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Constants.FuncHomeDirectoryName)
            : configured;

        return Path.GetFullPath(home);
    }
}
