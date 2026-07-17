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

namespace Azure.Functions.Cli.Tests.Commands;

public class HostWorkloadResolverTests
{
    private static readonly string _hostPackageId = HostWorkloadPackage.CurrentPackageId;

    private readonly IWorkloadProvider _workloadProvider = Substitute.For<IWorkloadProvider>();
    private readonly IWorkloadCatalog _workloadCatalog = Substitute.For<IWorkloadCatalog>();
    private readonly PackageSource _source = new("https://example.test/v3/index.json", "test");

    public HostWorkloadResolverTests()
    {
        _workloadProvider.GetContentWorkloads().Returns([]);
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

        HostWorkloadResolution.Installed installed = result.Should().BeOfType<HostWorkloadResolution.Installed>().Subject;
        installed.Workload.Should().BeSameAs(selectedHost);
        installed.HostVersion.Should().Be("4.1000.0");
        installed.ExplicitlyRequested.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_IgnoresHostAliasOnDifferentPackageId()
    {
        ContentWorkloadInfo wrongPackageHost = CreateHostWorkload("4.1000.0", packageId: "custom.host.package");
        ContentWorkloadInfo selectedHost = CreateHostWorkload("4.900.0", packageId: _hostPackageId);
        UseContentWorkloads(wrongPackageHost, selectedHost);
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(null, VersionRange.Parse("[4.0.0, 5.0.0)"), Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        result.Should().BeOfType<HostWorkloadResolution.Installed>().Which.Workload.Should().BeSameAs(selectedHost);
    }

    [Fact]
    public async Task ResolveAsync_IgnoresHostPackagesWithoutCurrentRidPackageId()
    {
        ContentWorkloadInfo nonRidHost = CreateHostWorkload("4.1100.0", packageId: "custom.host.package");
        ContentWorkloadInfo currentRidHost = CreateHostWorkload("4.1000.0");
        UseContentWorkloads(nonRidHost, currentRidHost);
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(null, VersionRange.Parse("[4.0.0, 5.0.0)"), Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        result.Should().BeOfType<HostWorkloadResolution.Installed>().Which.Workload.Should().BeSameAs(currentRidHost);
    }

    [Fact]
    public async Task ResolveAsync_RequestedHostOutsideProfileRange_Throws()
    {
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: "4.1100.0",
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: false);

        HostWorkloadResolutionException ex = (await FluentActions.Awaiting(() => resolver.ResolveAsync(context, CancellationToken.None)).Should().ThrowAsync<HostWorkloadResolutionException>()).Which;

        ex.Message.Should().Contain("outside profile host range");
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

        HostWorkloadResolution.Installed installed = result.Should().BeOfType<HostWorkloadResolution.Installed>().Subject;
        installed.ExplicitlyRequested.Should().BeTrue();
        installed.Workload.Should().BeSameAs(requestedHost);
    }

    [Fact]
    public async Task ResolveAsync_RequestedHostMissing_ReturnsCurrentRidPackage()
    {
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: "4.1000.0",
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        HostWorkloadResolution.InstallRequired installRequired = result.Should().BeOfType<HostWorkloadResolution.InstallRequired>().Subject;
        installRequired.HostVersion.Should().Be("4.1000.0");
        installRequired.PackageId.Should().Be(_hostPackageId);
    }

    [Fact]
    public async Task ResolveAsync_NoInstalledHost_ReturnsInstallRequired()
    {
        ResolvedPackage resolved = CreateResolvedPackage(_hostPackageId, "4.1048.199");
        _workloadCatalog.ResolveLatestVersionInRangeAsync(
                _hostPackageId,
                Arg.Any<VersionRange>(),
                (bool?)null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(resolved);
        DefaultHostWorkloadResolver resolver = NewResolver();
        var context = new HostWorkloadResolutionContext(
            RequestedHostVersion: null,
            ProfileHostVersionRange: VersionRange.Parse("[1.8.1, 4.1048.200)"),
            Offline: false);

        HostWorkloadResolution result = await resolver.ResolveAsync(context, CancellationToken.None);

        HostWorkloadResolution.InstallRequired installRequired = result.Should().BeOfType<HostWorkloadResolution.InstallRequired>().Subject;
        installRequired.HostVersion.Should().Be("4.1048.199");
        installRequired.PackageId.Should().Be(_hostPackageId);
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

        result.Should().BeOfType<HostWorkloadResolution.InstallRequired>()
            .Which.HostVersion.Should().Be("[1.8.1, 4.1048.200)");
        await _workloadCatalog.DidNotReceive().SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
        await _workloadCatalog.DidNotReceive().ResolveLatestVersionInRangeAsync(
            Arg.Any<string>(),
            Arg.Any<VersionRange>(),
            Arg.Any<bool?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidateStep_InstallRequiredAndOffline_ThrowsGracefulException()
    {
        IHostWorkloadResolver resolver = Substitute.For<IHostWorkloadResolver>();
        HostWorkloadResolution resolution = new HostWorkloadResolution.InstallRequired(
            "4.1000.0",
            "No compatible host installed",
            _hostPackageId);
        resolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);
        var step = new ValidateHostWorkloadInitializationStep(
            resolver,
            Substitute.For<IWorkloadInstaller>(),
            CreateWorkloadPaths(),
            Substitute.For<IProcessEnvironment>());
        StartInitializationStepContext context = NewStepContext(step, offline: true);

        GracefulException ex = (await FluentActions.Awaiting(() => step.ExecuteAsync(context, CancellationToken.None)).Should().ThrowAsync<GracefulException>()).Which;

        ex.Message.Should().Contain("--offline");
        context.State.HostVersion.Should().Be("4.1000.0");
    }

    [Fact]
    public async Task ValidateStep_InstallRequiredAndOnline_AddsInstallStep()
    {
        IHostWorkloadResolver resolver = Substitute.For<IHostWorkloadResolver>();
        HostWorkloadResolution resolution = new HostWorkloadResolution.InstallRequired(
            "4.1000.0",
            "No compatible host installed",
            _hostPackageId);
        resolver.ResolveAsync(Arg.Any<HostWorkloadResolutionContext>(), Arg.Any<CancellationToken>())
            .Returns(resolution);
        var step = new ValidateHostWorkloadInitializationStep(
            resolver,
            Substitute.For<IWorkloadInstaller>(),
            CreateWorkloadPaths(),
            Substitute.For<IProcessEnvironment>());
        StartInitializationStepContext context = NewStepContext(step, offline: false);

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        result.Message.Should().Be("No compatible host installed");
        context.DrainNextSteps().Single().Should().BeOfType<InstallHostWorkloadInitializationStep>();
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
                Arg.Any<bool?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(installResult);
        var step = new InstallHostWorkloadInitializationStep(installer, CreateWorkloadPaths(), _hostPackageId, "4.1000.0");
        StartInitializationStepContext context = NewStepContext(step, offline: false);

        StartInitializationStepResult result = await step.ExecuteAsync(context, CancellationToken.None);

        result.Message.Should().Be("Installed host 4.1000.0");
        context.State.HostVersion.Should().Be("4.1000.0");
        context.State.HostWorkload.Should().NotBeNull();
        context.State.HostWorkload.PackageId.Should().Be(_hostPackageId);
        await installer.Received(1).InstallFromCatalogAsync(
            packageId: _hostPackageId,
            version: Arg.Is<NuGetVersion?>(version => version != null && version.ToNormalizedString() == "4.1000.0"),
            source: null,
            includePrerelease: null,
            exact: true,
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
                Arg.Any<bool?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WorkloadInstallResult>(new WorkloadPackageNotFoundException("missing host")));
        var step = new InstallHostWorkloadInitializationStep(installer, CreateWorkloadPaths(), _hostPackageId, "4.1000.0");
        StartInitializationStepContext context = NewStepContext(step, offline: false);

        GracefulException ex = (await FluentActions.Awaiting(() => step.ExecuteAsync(context, CancellationToken.None)).Should().ThrowAsync<GracefulException>()).Which;

        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Be("missing host");
    }

    private void UseContentWorkloads(params ContentWorkloadInfo[] workloads)
        => _workloadProvider.GetContentWorkloads().Returns(workloads);

    private DefaultHostWorkloadResolver NewResolver()
        => new(_workloadProvider, _workloadCatalog);

    private ResolvedPackage CreateResolvedPackage(string packageId, string packageVersion)
        => new(packageId, NuGetVersion.Parse(packageVersion), _source);

    private static ContentWorkloadInfo CreateHostWorkload(string packageVersion, string? packageId = null)
    {
        packageId ??= _hostPackageId;
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
            PackageId = _hostPackageId,
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
            DemoMode: false,
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

    private static IWorkloadPaths CreateWorkloadPaths()
    {
        IWorkloadPaths workloadPaths = Substitute.For<IWorkloadPaths>();
        workloadPaths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => Path.Combine(Path.GetTempPath(), "workloads", (string)callInfo[0], (string)callInfo[1]));
        return workloadPaths;
    }
}
