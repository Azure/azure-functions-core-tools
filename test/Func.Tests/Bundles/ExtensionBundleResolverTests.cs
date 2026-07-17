// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.Cli.Bundles.Tests;

public class ExtensionBundleResolverTests
{
    private const string BundleId = BundleHelpers.StableBundleId;

    [Fact]
    public async Task HostJsonRangeOnly_PicksHighestInstalled()
    {
        ExtensionBundleResolution result = await Build([Row("4.10.0"), Row("4.22.0")])
            .ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        ExtensionBundleResolution.Resolved resolved = result.Should().BeOfType<ExtensionBundleResolution.Resolved>().Subject;
        resolved.Version.Should().Be("4.22.0");
        resolved.BundleId.Should().Be(BundleId);
        resolved.Path.Should().EndWith(Path.Combine("tools", "any"));
    }

    [Fact]
    public async Task HostAndProfile_NonEmptyIntersection_PicksHighestInIntersection()
    {
        ExtensionBundleResolution result = await Build([Row("4.10.0"), Row("4.22.0"), Row("5.1.0")])
            .ResolveAsync(Context(host: "[4.0.0, 6.0.0)", profile: "[4.15.0, 5.0.0)"));

        result.Should().BeOfType<ExtensionBundleResolution.Resolved>()
            .Which.Version.Should().Be("4.22.0");
    }

    [Fact]
    public async Task EmptyIntersection_ReturnsEmptyIntersectionVariant()
    {
        ExtensionBundleResolution result = await Build([Row("3.30.0"), Row("4.10.0")])
            .ResolveAsync(Context(host: "[3.*, 4.0.0)", profile: "[4.*, 5.0.0)"));

        ExtensionBundleResolution.EmptyIntersection empty = result.Should().BeOfType<ExtensionBundleResolution.EmptyIntersection>().Subject;
        empty.HighestVersionSatisfyingHostJsonOnly.Should().Be("3.30.0");
        empty.Hint.Should().Contain("[3.*, 4.0.0)");
    }

    [Fact]
    public async Task NoInstalledMatch_ReturnsNoCompatibleInstall()
    {
        ExtensionBundleResolution result = await Build([Row("4.10.0")])
            .ResolveAsync(Context(host: "[5.0.0, 6.0.0)"));

        ExtensionBundleResolution.NoCompatibleInstall none = result.Should().BeOfType<ExtensionBundleResolution.NoCompatibleInstall>().Subject;
        none.Hint.Should().Contain("4.10.0");
        none.Hint.Should().Contain("func workload install");
    }

    [Fact]
    public async Task NoInstalledRows_ReturnsWorkloadMissing()
    {
        ExtensionBundleResolution result = await Build([]).ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        result.Should().BeOfType<ExtensionBundleResolution.WorkloadMissing>()
            .Which.Hint.Should().Contain("func workload install");
    }

    [Fact]
    public async Task NoInstalledRows_KnownRuntime_HintIncludesFuncSetup()
    {
        ExtensionBundleResolution result = await Build([]).ResolveAsync(Context(host: "[4.0.0, 5.0.0)", workerRuntime: "go"));

        result.Should().BeOfType<ExtensionBundleResolution.WorkloadMissing>()
            .Which.Hint.Should().Contain("func setup --features go");
    }

    [Fact]
    public async Task NoInstalledMatch_KnownRuntime_HintIncludesFuncSetup()
    {
        ExtensionBundleResolution result = await Build([Row("4.10.0")])
            .ResolveAsync(Context(host: "[5.0.0, 6.0.0)", workerRuntime: "node"));

        result.Should().BeOfType<ExtensionBundleResolution.NoCompatibleInstall>()
            .Which.Hint.Should().Contain("func setup --features node");
    }

    [Fact]
    public async Task TelemetryReason_IsOkOnResolved()
    {
        var telemetry = new RecordingTelemetry();
        await Build([Row("4.22.0")], telemetry).ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        BundleResolveEvent evt = telemetry.Events.Should().ContainSingle().Subject;
        evt.Reason.Should().Be(BundleResolveReason.Ok);
        evt.ResolvedVersion.Should().Be("4.22.0");
    }

    [Fact]
    public async Task TelemetryReason_IsWorkloadMissing()
    {
        var telemetry = new RecordingTelemetry();
        await Build([], telemetry).ResolveAsync(Context(host: "[4.0.0, 5.0.0)"));

        telemetry.Events.Should().ContainSingle().Which.Reason.Should().Be(BundleResolveReason.WorkloadMissing);
    }

    [Fact]
    public async Task TelemetryReason_IsEmptyIntersection()
    {
        var telemetry = new RecordingTelemetry();
        await Build([Row("4.22.0")], telemetry).ResolveAsync(Context(host: "[3.*, 4.0.0)", profile: "[4.*, 5.0.0)"));

        telemetry.Events.Should().ContainSingle().Which.Reason.Should().Be(BundleResolveReason.EmptyIntersection);
    }

    [Fact]
    public async Task TelemetryReason_IsNoCompatibleInstall()
    {
        var telemetry = new RecordingTelemetry();
        await Build([Row("3.0.0")], telemetry).ResolveAsync(Context(host: "[5.0.0, 6.0.0)"));

        telemetry.Events.Should().ContainSingle().Which.Reason.Should().Be(BundleResolveReason.NoCompatibleInstall);
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

    private static ExtensionBundleProjectContext Context(string host, string? profile = null, string workerRuntime = "dotnet") =>
        new(BundleId, host, workerRuntime, ProfileName: profile is null ? null : "stable", ProfileBundleVersionRange: profile);

    private sealed class RecordingTelemetry : IBundleResolveTelemetry
    {
        public List<BundleResolveEvent> Events { get; } = [];

        public void Record(BundleResolveEvent evt) => Events.Add(evt);
    }
}
