// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Commands.Start.Initialization.Rendering;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class HostWorkloadResolverTests
{
    private readonly IWorkloadProvider _workloadProvider = Substitute.For<IWorkloadProvider>();
    private readonly IWorkloadCatalog _workloadCatalog = Substitute.For<IWorkloadCatalog>();
    private readonly PackageSource _source = new("https://example.test/v3/index.json", "test");

    public HostWorkloadResolverTests()
    {
        _workloadProvider.GetContentWorkloads().Returns([]);
        _workloadCatalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([CreateCatalogResult("Azure.Functions.Cli.Workloads.Host", "5.0.0")]);
    }

    [Fact]
    public async Task ResolveAsync_PicksHighestInstalledHostWithinProfileRange()
    {
        ContentWorkloadInfo oldHost = CreateHostWorkload("4.900.0");
        ContentWorkloadInfo selectedHost = CreateHostWorkload("4.1000.0");
        ContentWorkloadInfo tooNewHost = CreateHostWorkload("4.1100.0");
        UseContentWorkloads(oldHost, selectedHost, tooNewHost);
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: null,
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        HostWorkloadResolution.Installed installed = Assert.IsType<HostWorkloadResolution.Installed>(result);
        Assert.Same(selectedHost, installed.Workload);
        Assert.Equal("4.1000.0", installed.HostVersion);
        Assert.False(installed.ExplicitlyRequested);
    }

    [Fact]
    public async Task ResolveAsync_UsesHostAliasInsteadOfFixedPackageId()
    {
        ContentWorkloadInfo host = CreateHostWorkload("4.1000.0", packageId: "custom.host.package");
        UseContentWorkloads(host);
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(null, VersionRange.Parse("[4.0.0, 5.0.0)"), Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        HostWorkloadResolution.Installed installed = Assert.IsType<HostWorkloadResolution.Installed>(result);
        Assert.Same(host, installed.Workload);
    }

    [Fact]
    public async Task ResolveAsync_RequestedHostOutsideProfileRange_Throws()
    {
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: "4.1100.0",
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: false);

        HostWorkloadResolutionException ex = await Assert.ThrowsAsync<HostWorkloadResolutionException>(
            () => resolver.ResolveAsync(context, CancellationToken.None));

        Assert.Contains("outside profile host range", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_RequestedHostInstalled_ReturnsExplicitInstalled()
    {
        ContentWorkloadInfo requestedHost = CreateHostWorkload("4.1000.0");
        UseContentWorkloads(CreateHostWorkload("4.900.0"), requestedHost);
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: "4.1000.0",
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        HostWorkloadResolution.Installed installed = Assert.IsType<HostWorkloadResolution.Installed>(result);
        Assert.True(installed.ExplicitlyRequested);
        Assert.Same(requestedHost, installed.Workload);
    }

    [Fact]
    public async Task ResolveAsync_NoInstalledHost_ReturnsInstallRequired()
    {
        ResolvedPackage resolved = CreateResolvedPackage("Azure.Functions.Cli.Workloads.Host", "4.1048.199");
        _workloadCatalog.ResolveLatestVersionInRangeAsync(
                "Azure.Functions.Cli.Workloads.Host",
                Arg.Any<VersionRange>(),
                false,
                null,
                Arg.Any<CancellationToken>())
            .Returns(resolved);
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: null,
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        HostWorkloadResolution.InstallRequired installRequired = Assert.IsType<HostWorkloadResolution.InstallRequired>(result);
        Assert.Equal("4.1048.199", installRequired.HostVersion);
    }

    [Fact]
    public async Task ResolveAsync_NoInstalledHostAndOffline_DoesNotQueryCatalog()
    {
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: null,
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: true);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        HostWorkloadResolution.InstallRequired installRequired = Assert.IsType<HostWorkloadResolution.InstallRequired>(result);
        Assert.Equal("[1.8.1, 4.1048.200)", installRequired.HostVersion);
        await _workloadCatalog.DidNotReceive().SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
        await _workloadCatalog.DidNotReceive().ResolveLatestVersionInRangeAsync(
            Arg.Any<string>(),
            Arg.Any<VersionRange>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateStep_InstallRequiredAndOffline_ThrowsGracefulException()
    {
        IHostWorkloadResolver resolver = Substitute.For<IHostWorkloadResolver>();
        HostWorkloadResolution resolution = new HostWorkloadResolution.InstallRequired("4.1000.0", "No compatible host installed");
        resolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);
        var step = new ValidateHostWorkloadInitializationStep(resolver, Substitute.For<IWorkloadInstaller>());
        StartInitializationStepContext context = NewStepContext(step, offline: true);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => step.ExecuteAsync(context, CancellationToken.None));

        Assert.Contains("--offline", ex.Message);
        Assert.Equal("4.1000.0", context.State.HostVersion);
    }

    [Fact]
    public async Task ValidateStep_InstallRequiredAndOnline_AddsInstallStep()
    {
        IHostWorkloadResolver resolver = Substitute.For<IHostWorkloadResolver>();
        HostWorkloadResolution resolution = new HostWorkloadResolution.InstallRequired("4.1000.0", "No compatible host installed");
        resolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);
        var step = new ValidateHostWorkloadInitializationStep(resolver, Substitute.For<IWorkloadInstaller>());
        StartInitializationStepContext context = NewStepContext(step, offline: false);

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("No compatible host installed", result.Message);
        Assert.IsType<InstallHostWorkloadInitializationStep>(context.DrainNextSteps().Single());
    }

    [Fact]
    public async Task InstallStep_InstallsHostFromCatalogAndUpdatesState()
    {
        IWorkloadInstaller installer = Substitute.For<IWorkloadInstaller>();
        var installResult = new WorkloadInstallResult(CreateHostEntry("4.1000.0"), AlreadyInstalled: false);
        installer.InstallFromCatalogAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(installResult);
        var step = new InstallHostWorkloadInitializationStep(installer, "4.1000.0");
        StartInitializationStepContext context = NewStepContext(step, offline: false);

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal("Installed host 4.1000.0", result.Message);
        Assert.Equal("4.1000.0", context.State.HostVersion);
        await installer.Received(1).InstallFromCatalogAsync(
            packageId: "host",
            version: Arg.Is<NuGetVersion?>(version => version != null && version.ToNormalizedString() == "4.1000.0"),
            source: null,
            includePrerelease: false,
            exact: false,
            force: false,
            progress: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallStep_CatalogFailure_ThrowsGracefulException()
    {
        IWorkloadInstaller installer = Substitute.For<IWorkloadInstaller>();
        installer.InstallFromCatalogAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WorkloadInstallResult>(new WorkloadPackageNotFoundException("missing host")));
        var step = new InstallHostWorkloadInitializationStep(installer, "4.1000.0");
        StartInitializationStepContext context = NewStepContext(step, offline: false);

        GracefulException ex = await Assert.ThrowsAsync<GracefulException>(
            () => step.ExecuteAsync(context, CancellationToken.None));

        Assert.True(ex.IsUserError);
        Assert.Equal("missing host", ex.Message);
    }

    private void UseContentWorkloads(params ContentWorkloadInfo[] workloads)
        => _workloadProvider.GetContentWorkloads().Returns(workloads);

    private DefaultHostWorkloadResolver NewResolver()
        => new(_workloadProvider, _workloadCatalog);

    private CatalogSearchResult CreateCatalogResult(string packageId, string packageVersion)
        => new(packageId, NuGetVersion.Parse(packageVersion), Title: null, Description: null, Aliases: ["host"], _source);

    private ResolvedPackage CreateResolvedPackage(string packageId, string packageVersion)
        => new(packageId, NuGetVersion.Parse(packageVersion), _source);

    private static ContentWorkloadInfo CreateHostWorkload(string packageVersion, string packageId = "Azure.Functions.Cli.Workloads.Host")
    {
        string installDirectory = Path.Combine(Path.GetTempPath(), "workloads", packageId, packageVersion);
        return new ContentWorkloadInfo(
            packageId,
            packageVersion,
            ["host"],
            installDirectory,
            Path.Combine(installDirectory, "tools", "any"),
            packageId,
            string.Empty);
    }

    private static WorkloadEntry CreateHostEntry(string packageVersion)
        => new()
        {
            PackageId = "Azure.Functions.Cli.Workloads.Host",
            PackageVersion = packageVersion,
            Kind = WorkloadKind.Content,
            Aliases = ["host"],
            DisplayName = "Azure Functions host",
            Description = string.Empty,
        };

    private static StartInitializationStepContext NewStepContext(IStartInitializationStep step, bool offline)
    {
        var options = new StartCommandOptions(
            WorkingDirectory.FromExplicit(Path.GetTempPath()),
            Port: null,
            Cors: [],
            CorsCredentials: false,
            Functions: [],
            NoBuild: false,
            EnableAuth: false,
            RequestedProfileName: null,
            RequestedHostVersion: null,
            Offline: offline,
            OutputMode.Plain,
            NoTui: true,
            LogFilePath: null,
            DemoFunctionCount: 0,
            DemoSpeedMultiplier: 1.0,
            DemoAutoExit: true);
        var context = new StartInitializationContext(options, "5.0.0-test", IsInteractive: false, CanPrompt: false);
        var state = new StartInitializationState();
        IStartInitializationRenderer renderer = Substitute.For<IStartInitializationRenderer>();

        return new StartInitializationStepContext(
            context,
            state,
            step,
            renderer,
            TimeProvider.System);
    }
}
