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
    public const string BundlePayloadSubpath = "tools/any";

    public const string StableBundleId = "Microsoft.Azure.Functions.ExtensionBundle";
    public const string PreviewBundleId = "Microsoft.Azure.Functions.ExtensionBundle.Preview";

    private static readonly HashSet<string> _previewReleaseLabels =
        new(StringComparer.OrdinalIgnoreCase) { "preview", "experimental" };

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
            // Stable host.json id → only stable workload versions.
            // Preview host.json id → only preview/experimental prerelease labels.
            if (!MatchesBundleIdFilter(bundleId, version))
            {
                continue;
            }

            string payloadPath = Path.Combine(row.InstallDirectory, BundlePayloadSubpath);
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
            if (!version.IsPrerelease)
            {
                return false;
            }

            foreach (string label in version.ReleaseLabels)
            {
                if (_previewReleaseLabels.Contains(label))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
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
