// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Filters installed bundle workload rows by host.json bundle id and projects them to the bundle payload path.
/// Workload version IS the bundle version (1:1 model, spec §5.1).
/// </summary>
internal sealed class InstalledBundleScanner(IInstalledBundleWorkloads installed)
{
    // Fixed subpath inside the workload install dir that carries the bundle zip layout.
    // Matches the pack location in src/Workloads/Tools/ExtensionBundles/DownloadDefaultBundle.targets.
    private static readonly string _bundlePayloadSubpath = Path.Combine("tools", "any");

    public const string StableBundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    public const string PreviewBundleId = "Microsoft.Azure.Functions.ExtensionBundle.Preview";
    public const string ExperimentalBundleId = "Microsoft.Azure.Functions.ExtensionBundle.Experimental";

    private const string PreviewLabel = "preview";
    private const string ExperimentalLabel = "experimental";

    private readonly IInstalledBundleWorkloads _installed = installed ?? throw new ArgumentNullException(nameof(installed));

    public async Task<ScanResult> ScanAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        IReadOnlyList<InstalledBundleWorkload> rows = await _installed.ListInstalledAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return ScanResult.Empty;
        }

        List<InstalledBundle> filtered = [];
        foreach (InstalledBundleWorkload row in rows)
        {
            if (!NuGetVersion.TryParse(row.PackageVersion, out NuGetVersion? version))
            {
                continue;
            }

            // TODO(spec §9 Q1): confirm/refine prerelease filter rule.
            // Stable host.json id → workload versions with no prerelease label.
            // Preview host.json id → workload versions whose prerelease label is `preview`.
            // Experimental host.json id → workload versions whose prerelease label is `experimental`.
            if (!MatchesBundleIdFilter(bundleId, version))
            {
                continue;
            }

            string payloadPath = Path.Combine(row.InstallDirectory, _bundlePayloadSubpath);
            filtered.Add(new InstalledBundle(version, payloadPath));
        }

        filtered.Sort((a, b) => b.Version.CompareTo(a.Version));
        return new ScanResult(rows.Count, filtered);
    }

    private static bool MatchesBundleIdFilter(string bundleId, NuGetVersion version)
    {
        if (string.Equals(bundleId, StableBundleId, StringComparison.OrdinalIgnoreCase))
        {
            return !version.IsPrerelease;
        }

        if (string.Equals(bundleId, PreviewBundleId, StringComparison.OrdinalIgnoreCase))
        {
            return HasReleaseLabel(version, PreviewLabel);
        }

        if (string.Equals(bundleId, ExperimentalBundleId, StringComparison.OrdinalIgnoreCase))
        {
            return HasReleaseLabel(version, ExperimentalLabel);
        }

        return true;
    }

    private static bool HasReleaseLabel(NuGetVersion version, string label)
    {
        if (!version.IsPrerelease)
        {
            return false;
        }

        foreach (string candidate in version.ReleaseLabels)
        {
            if (string.Equals(candidate, label, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed record InstalledBundle(NuGetVersion Version, string Path);

/// <summary>
/// Result of <see cref="InstalledBundleScanner.ScanAsync"/>. <see cref="TotalInstalledRows"/>
/// distinguishes "workload not installed at all" (0) from "installed but filtered out" (&gt;0).
/// </summary>
internal sealed record ScanResult(int TotalInstalledRows, IReadOnlyList<InstalledBundle> FilteredVersions)
{
    public static ScanResult Empty { get; } = new(0, []);
}
