// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Cli.Bundles.Tests;

public class ExtensionBundleResolverTests
{
    private const string BundleId = InstalledBundleScanner.StableBundleId;

    [Fact]
    public async Task HostJsonRangeOnly_PicksHighestInstalled()
    {
        ExtensionBundleResolution result = await Build([Row("4.10.0"), Row("4.22.0")])
            .ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        ExtensionBundleResolution.Resolved resolved = Assert.IsType<ExtensionBundleResolution.Resolved>(result);
        Assert.Equal("4.22.0", resolved.Version);
        Assert.Equal(BundleId, resolved.BundleId);
        Assert.EndsWith(Path.Combine("tools", "any"), resolved.Path);
    }

    [Fact]
    public async Task HostAndProfile_NonEmptyIntersection_PicksHighestInIntersection()
    {
        ExtensionBundleResolution result = await Build([Row("4.10.0"), Row("4.22.0"), Row("5.1.0")])
            .ResolveAsync(Context(host: "[4.0.0, 6.0.0)", profile: "[4.15.0, 5.0.0)"));

        ExtensionBundleResolution.Resolved resolved = Assert.IsType<ExtensionBundleResolution.Resolved>(result);
        Assert.Equal("4.22.0", resolved.Version);
    }

    [Fact]
    public async Task EmptyIntersection_ReturnsEmptyIntersectionVariant()
    {
        ExtensionBundleResolution result = await Build([Row("3.30.0"), Row("4.10.0")])
            .ResolveAsync(Context(host: "[3.*, 4.0.0)", profile: "[4.*, 5.0.0)"));

        ExtensionBundleResolution.EmptyIntersection empty = Assert.IsType<ExtensionBundleResolution.EmptyIntersection>(result);
        Assert.Equal("3.30.0", empty.HighestVersionSatisfyingHostJsonOnly);
        Assert.Contains("[3.*, 4.0.0)", empty.Hint);
    }

    [Fact]
    public async Task NoInstalledMatch_ReturnsNoCompatibleInstall()
    {
        ExtensionBundleResolution result = await Build([Row("4.10.0")])
            .ResolveAsync(Context(host: "[5.0.0, 6.0.0)"));

        ExtensionBundleResolution.NoCompatibleInstall none = Assert.IsType<ExtensionBundleResolution.NoCompatibleInstall>(result);
        Assert.Contains("4.10.0", none.Hint);
        Assert.Contains("func workload install", none.Hint);
    }

    [Fact]
    public async Task NoInstalledRows_ReturnsWorkloadMissing()
    {
        ExtensionBundleResolution result = await Build([]).ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        ExtensionBundleResolution.WorkloadMissing missing = Assert.IsType<ExtensionBundleResolution.WorkloadMissing>(result);
        Assert.Contains("func workload install", missing.Hint);
    }

    [Fact]
    public async Task TelemetryReason_IsOkOnResolved()
    {
        var telemetry = new RecordingTelemetry();
        await Build([Row("4.22.0")], telemetry).ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        BundleResolveEvent evt = Assert.Single(telemetry.Events);
        Assert.Equal(BundleResolveReason.Ok, evt.Reason);
        Assert.Equal("4.22.0", evt.ResolvedVersion);
    }

    [Fact]
    public async Task TelemetryReason_IsWorkloadMissing()
    {
        var telemetry = new RecordingTelemetry();
        await Build([], telemetry).ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        BundleResolveEvent evt = Assert.Single(telemetry.Events);
        Assert.Equal(BundleResolveReason.WorkloadMissing, evt.Reason);
    }

    [Fact]
    public async Task TelemetryReason_IsEmptyIntersection()
    {
        var telemetry = new RecordingTelemetry();
        await Build([Row("4.22.0")], telemetry).ResolveAsync(Context(host: "[3.*, 4.0.0)", profile: "[4.*, 5.0.0)"));

        BundleResolveEvent evt = Assert.Single(telemetry.Events);
        Assert.Equal(BundleResolveReason.EmptyIntersection, evt.Reason);
    }

    [Fact]
    public async Task TelemetryReason_IsNoCompatibleInstall()
    {
        var telemetry = new RecordingTelemetry();
        await Build([Row("3.0.0")], telemetry).ResolveAsync(Context(host: "[5.0.0, 6.0.0)"));

        BundleResolveEvent evt = Assert.Single(telemetry.Events);
        Assert.Equal(BundleResolveReason.NoCompatibleInstall, evt.Reason);
    }

    private static ExtensionBundleResolver Build(IReadOnlyList<InstalledBundleWorkload> rows, IBundleResolveTelemetry? telemetry = null)
    {
        var scanner = new InstalledBundleScanner(new FakeInstalledBundleWorkloads(rows));
        return new ExtensionBundleResolver(
            scanner,
            telemetry ?? NullBundleResolveTelemetry.Instance,
            NullLogger<ExtensionBundleResolver>.Instance);
    }

    private static InstalledBundleWorkload Row(string version)
        => new(version, "/install/" + version);

    private static ExtensionBundleProjectContext Context(string host, string? profile = null) =>
        new(BundleId, host, "dotnet", ProfileName: profile is null ? null : "stable", ProfileBundleVersionRange: profile);

    private sealed class RecordingTelemetry : IBundleResolveTelemetry
    {
        public List<BundleResolveEvent> Events { get; } = [];

        public void Record(BundleResolveEvent evt) => Events.Add(evt);
    }
}
