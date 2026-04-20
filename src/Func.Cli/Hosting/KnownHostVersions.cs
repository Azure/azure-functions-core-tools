// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Maintains the list of host versions that have been tested with this CLI version.
/// Updated with each CLI release based on CI nightly testing results.
/// </summary>
public static class KnownHostVersions
{
    /// <summary>
    /// The recommended host version for new installations.
    /// This is the most recent stable version verified to work with this CLI.
    /// </summary>
    public const string RecommendedVersion = "4.1047.100";

    /// <summary>
    /// NuGet package ID for the host runtime.
    /// </summary>
    public const string HostPackageId = "Microsoft.Azure.WebJobs.Script.WebHost";

    /// <summary>
    /// Host versions that have been tested and verified with this CLI version.
    /// Sorted descending (newest first).
    /// </summary>
    private static readonly string[] _verifiedVersions =
    [
        "4.1047.100",
    ];

    /// <summary>
    /// Checks if a host version has been tested with this CLI version.
    /// </summary>
    public static bool IsVerified(string version)
    {
        return Array.Exists(_verifiedVersions, v => string.Equals(v, version, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns all verified versions (newest first).
    /// </summary>
    public static IReadOnlyList<string> GetVerifiedVersions() => _verifiedVersions;
}
