// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Filesystem layout for installed workloads. Bound from <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// at startup so tests can inject a temp directory without mutating process-global env state.
/// Default <see cref="Home"/> is <c>~/.azure-functions</c>; override via the
/// <c>FUNC_CLI_HOME</c> environment variable.
/// </summary>
internal sealed class WorkloadPathsOptions
{
    /// <summary>
    /// Filename of the global workload manifest within <see cref="Home"/>.
    /// </summary>
    public const string GlobalManifestFileName = "workloads.json";

    /// <summary>
    /// Root for everything the func CLI persists for the user. Settable so
    /// the Options binder can populate it from configuration; in tests, set
    /// directly to a temp path.
    /// </summary>
    public string Home { get; set; } = DefaultHome();

    /// <summary>
    /// Directory containing all installed workload packages.
    /// </summary>
    public string WorkloadsRoot => Path.Combine(Home, "workloads");

    /// <summary>
    /// Absolute path to the global workload manifest file.
    /// </summary>
    public string GlobalManifestPath => Path.Combine(Home, GlobalManifestFileName);

    /// <summary>
    /// Per-package install directory inside <see cref="WorkloadsRoot"/>, namespaced by version.
    /// </summary>
    public string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);

    private static string DefaultHome()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".azure-functions");
}
