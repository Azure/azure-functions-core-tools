// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Configuration + computed filesystem layout for installed workloads.
/// Bound from the <c>Workloads</c> configuration section at startup; the
/// corresponding environment variable is <c>FUNC_CLI_Workloads__Home</c>.
/// </summary>
/// <remarks>
/// Only <see cref="Home"/> is settable. Everything else is computed from it
/// and exposed through <see cref="IWorkloadPaths"/>.
/// </remarks>
internal sealed class WorkloadPathsOptions : IWorkloadPaths
{
    /// <summary>
    /// Filename of the global workload manifest within <see cref="Home"/>.
    /// </summary>
    public const string GlobalManifestFileName = "workloads.json";

    /// <summary>
    /// Root directory the func CLI persists workloads under. Defaults to
    /// <c>~/.azure-functions</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public string Home { get; set; } = DefaultHome();

    /// <inheritdoc />
    public string WorkloadsRoot => Path.Combine(Home, "workloads");

    /// <inheritdoc />
    public string GlobalManifestPath => Path.Combine(Home, GlobalManifestFileName);

    /// <inheritdoc />
    public string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);

    private static string DefaultHome()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName);
}
