// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Filesystem layout for installed workloads. Honors the <c>FUNC_CLI_HOME</c>
/// environment variable so tests can redirect installs to a temp folder.
/// </summary>
internal static class WorkloadPaths
{
    public const string GlobalManifestFileName = "workloads.json";

    /// <summary>Root for everything Core Tools persists for the user (<c>~/.azure-functions</c>).</summary>
    public static string Home
    {
        get
        {
            var overrideHome = Environment.GetEnvironmentVariable("FUNC_CLI_HOME");
            if (!string.IsNullOrEmpty(overrideHome))
            {
                return overrideHome;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".azure-functions");
        }
    }

    public static string WorkloadsRoot => Path.Combine(Home, "workloads");

    public static string GlobalManifestPath => Path.Combine(Home, GlobalManifestFileName);

    public static string GetInstallDirectory(string packageId, string version)
        => Path.Combine(WorkloadsRoot, packageId, version);
}
