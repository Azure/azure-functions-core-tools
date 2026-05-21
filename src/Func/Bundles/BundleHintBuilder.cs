// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;

namespace Azure.Functions.Cli.Bundles;

/// <summary>

/// Builds the user-facing <c>Hint</c> strings surfaced in resolver failures.

/// </summary>
internal static class BundleHintBuilder
{
    private const string InstallCommandPrefix =
        $"func workload install {IInstalledBundleWorkloads.BundleWorkloadPackageId}";

    public static string WorkloadMissing() =>
        "host.json declares an extensionBundle but no bundles workload is installed. Install one with:" + Environment.NewLine +
        $"  {InstallCommandPrefix}@<version>";

    public static string EmptyIntersection(
        string bundleId,
        string hostJsonRange,
        string profileRange,
        string? highestVersionSatisfyingHostJsonOnly,
        string? profileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Could not satisfy extension bundle '{bundleId}': host.json range {hostJsonRange} has no overlap with profile range {profileRange}.");

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Active profile: {profileName}.");
        }

        if (!string.IsNullOrWhiteSpace(highestVersionSatisfyingHostJsonOnly))
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"Highest version matching host.json alone: {highestVersionSatisfyingHostJsonOnly}.");
        }

        sb.Append("Widen extensionBundle.version in host.json or pick a different profile.");
        return sb.ToString();
    }

    public static string NoCompatibleInstall(
        string bundleId,
        string constraintRange,
        IReadOnlyList<string> installedVersions,
        string? suggestedVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"No installed bundle workload satisfies '{bundleId}' {constraintRange}.");

        sb.AppendLine(installedVersions.Count > 0
            ? string.Format(CultureInfo.InvariantCulture, "Installed versions: {0}.", string.Join(", ", installedVersions))
            : "No bundle workload is installed.");

        sb.AppendLine("Install a satisfying version with:");
        if (string.IsNullOrWhiteSpace(suggestedVersion))
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {InstallCommandPrefix}@<version> --force");
        }
        else
        {
            sb.Append(CultureInfo.InvariantCulture, $"  {InstallCommandPrefix}@{suggestedVersion} --force");
        }

        return sb.ToString();
    }
}
