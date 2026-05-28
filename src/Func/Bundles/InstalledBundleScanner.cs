// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;
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

    private readonly IInstalledBundleWorkloads _installed = installed ?? throw new ArgumentNullException(nameof(installed));

    public Task<ScanResult> ScanAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        return ScanAsync(BundleHelpers.GetBundleChannel(bundleId), cancellationToken);
    }

    public async Task<ScanResult> ScanAsync(BundleChannel channel, CancellationToken cancellationToken = default)
    {
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
            if (BundleHelpers.GetBundleChannel(version) != channel)
            {
                continue;
            }

            string payloadPath = Path.Combine(row.InstallDirectory, _bundlePayloadSubpath);
            filtered.Add(new InstalledBundle(version, payloadPath));
        }

        filtered.Sort((a, b) => b.Version.CompareTo(a.Version));
        return new ScanResult(rows.Count, filtered);
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
