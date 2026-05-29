// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Default <see cref="IExtensionBundleResolver"/>. Scans installed bundle workload rows and
/// picks the highest version satisfying the host.json/profile intersection. Performs no network I/O.
/// </summary>
internal sealed class ExtensionBundleResolver(
    InstalledBundleScanner scanner,
    IBundleResolveTelemetry telemetry,
    ILogger<ExtensionBundleResolver> logger) : IExtensionBundleResolver
{
    private readonly InstalledBundleScanner _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
    private readonly IBundleResolveTelemetry _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    private readonly ILogger<ExtensionBundleResolver> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<ExtensionBundleResolution> ResolveAsync(
        ExtensionBundleProjectContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sw = Stopwatch.StartNew();

        ScanResult scan = await _scanner.ScanAsync(context.BundleId, cancellationToken);

        if (scan.TotalInstalledRows == 0)
        {
            return Record(context, context.HostJsonVersionRange,
                new ExtensionBundleResolution.WorkloadMissing(BundleHintBuilder.WorkloadMissing(context.WorkerRuntime)),
                sw);
        }

        IReadOnlyList<InstalledBundle> installed = scan.FilteredVersions;

        VersionRange? constraint;
        try
        {
            constraint = VersionRangeIntersection.Intersect(context.HostJsonVersionRange, context.ProfileBundleVersionRange);
        }
        catch (ArgumentException ex)
        {
            _logger.LogDebug(ex, "[bundle-resolve] malformed range; failing closed.");
            return Record(context, context.HostJsonVersionRange,
                MakeNoCompatibleInstall(context, context.HostJsonVersionRange, installed),
                sw);
        }

        if (constraint is null)
        {
            return Record(context, FormatIntersection(context),
                MakeEmptyIntersection(context, installed),
                sw);
        }

        _logger.LogDebug("[bundle-resolve] installed versions (filtered) for {BundleId}: {Versions}; constraint {Constraint}.",
            context.BundleId,
            string.Join(", ", installed.Select(i => i.Version.ToNormalizedString())),
            RangeText(constraint));

        InstalledBundle? best = PickBest(installed, constraint);
        if (best is not null)
        {
            string bundleVersion = ToBundleVersion(best.Version);
            _logger.LogInformation("Using extension bundle {BundleId} {Version} from {Path}.",
                context.BundleId, bundleVersion, best.Path);

            return Record(context, RangeText(constraint),
                new ExtensionBundleResolution.Resolved(
                    context.BundleId, bundleVersion, best.Path, RuntimeWarning: null),
                sw);
        }

        return Record(context, RangeText(constraint),
            MakeNoCompatibleInstall(context, RangeText(constraint), installed),
            sw);
    }

    private static ExtensionBundleResolution.NoCompatibleInstall MakeNoCompatibleInstall(
        ExtensionBundleProjectContext context,
        string constraintRange,
        IReadOnlyList<InstalledBundle> installed)
    {
        IReadOnlyList<string> installedVersions = [.. installed.Select(b => b.Version.ToNormalizedString())];
        string? suggested = installedVersions.FirstOrDefault();
        string hint = BundleHintBuilder.NoCompatibleInstall(context.BundleId, constraintRange, installedVersions, suggested, context.WorkerRuntime);

        return new ExtensionBundleResolution.NoCompatibleInstall(constraintRange, installedVersions, hint);
    }

    private ExtensionBundleResolution.EmptyIntersection MakeEmptyIntersection(
        ExtensionBundleProjectContext context,
        IReadOnlyList<InstalledBundle> installed)
    {
        string profileRange = context.ProfileBundleVersionRange ?? string.Empty;
        string? highestHostOnly = FindHighestSatisfyingHostJsonOnly(context, installed);
        _logger.LogDebug("[bundle-resolve] empty intersection of {Host} and {Profile}.",
            context.HostJsonVersionRange, profileRange);

        string hint = BundleHintBuilder.EmptyIntersection(
            context.BundleId, context.HostJsonVersionRange, profileRange, highestHostOnly, context.ProfileName);

        return new ExtensionBundleResolution.EmptyIntersection(
            context.HostJsonVersionRange, profileRange, highestHostOnly, hint);
    }

    private static string? FindHighestSatisfyingHostJsonOnly(
        ExtensionBundleProjectContext context,
        IReadOnlyList<InstalledBundle> installed)
    {
        try
        {
            var host = VersionRange.Parse(context.HostJsonVersionRange);
            NuGetVersion? best = VersionRangeIntersection.FindBest(installed.Select(b => b.Version), host);
            return best?.ToNormalizedString();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static InstalledBundle? PickBest(IReadOnlyList<InstalledBundle> installed, VersionRange constraint)
    {
        NuGetVersion? bestVersion = VersionRangeIntersection.FindBest(installed.Select(b => b.Version), constraint);
        return bestVersion is null ? null : installed.First(b => b.Version.Equals(bestVersion));
    }

    private static string RangeText(VersionRange range) => range.OriginalString ?? range.ToString();

    // Workload pkg version maps 1:1 to the bundle payload version, optionally with
    // a channel prerelease label (e.g. 4.35.0-preview); the user-facing bundle
    // version is always the 3-part bundle payload version.
    private static string ToBundleVersion(NuGetVersion version)
        => $"{version.Major}.{version.Minor}.{version.Patch}";

    private static string FormatIntersection(ExtensionBundleProjectContext context)
        => string.IsNullOrWhiteSpace(context.ProfileBundleVersionRange)
            ? context.HostJsonVersionRange
            : $"{context.HostJsonVersionRange} ∩ {context.ProfileBundleVersionRange}";

    private ExtensionBundleResolution Record(
        ExtensionBundleProjectContext context,
        string constraintRange,
        ExtensionBundleResolution resolution,
        Stopwatch sw)
    {
        BundleResolveEvent evt = BundleResolveEventFactory.FromResolution(context, constraintRange, resolution, sw.ElapsedMilliseconds);
        _telemetry.Record(evt);
        _logger.LogDebug("[bundle-resolve] event {@Event}.", evt);
        return resolution;
    }
}
