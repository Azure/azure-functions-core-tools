// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workers;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Workers;

public class DefaultFunctionsWorkerInstallerTests
{
    private const string WorkerRuntime = "node";
    private const string WorkerPackageId = "Azure.Functions.Cli.Workloads.Workers.node";

    private readonly IWorkloadCatalog _workloadCatalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadInstaller _workloadInstaller = Substitute.For<IWorkloadInstaller>();
    private readonly IWorkloadPaths _workloadPaths = Substitute.For<IWorkloadPaths>();
    private readonly IWorkerConfigFileSystem _fileSystem = Substitute.For<IWorkerConfigFileSystem>();

    public DefaultFunctionsWorkerInstallerTests()
    {
        _workloadPaths.GetInstallDirectory(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo => Path.Combine(Path.GetTempPath(), "workloads", (string)callInfo[0], (string)callInfo[1]));
        _fileSystem.FileExists(Arg.Any<string>()).Returns(true);
    }

    [Fact]
    public async Task InstallAsync_ProfileRangeInstallsResolvedVersionAndReturnsWorker()
    {
        var workerId = new FunctionsWorkerId(WorkerRuntime);
        var range = VersionRange.Parse("[3.13.0]");
        Dictionary<string, VersionRange> workerRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            [WorkerRuntime] = range,
        };
        _workloadCatalog.ResolveLatestVersionInRangeAsync(
                WorkerPackageId,
                range,
                includePrerelease: null,
                source: null,
                Arg.Any<CancellationToken>())
            .Returns(new ResolvedPackage(
                WorkerPackageId,
                NuGetVersion.Parse("3.13.0"),
                new PackageSource("https://example.test/v3/index.json")));
        _workloadInstaller.InstallFromCatalogAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(CreateWorkerEntry("3.13.0"), AlreadyInstalled: false));
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        FunctionsWorkerInstallResult result = await installer.InstallAsync(workerId, workerRanges, CancellationToken.None);

        result.Worker.WorkerRuntime.Should().Be(WorkerRuntime);
        result.Worker.Version.Should().Be("3.13.0");
        result.WorkloadInstallResult.AlreadyInstalled.Should().BeFalse();
        result.Worker.WorkerConfigPath.Should().Be(Path.Combine(GetInstallDirectory("3.13.0"), "tools", "any", "worker.config.json"));
        await _workloadInstaller.Received(1).InstallFromCatalogAsync(
            WorkerPackageId,
            Arg.Is<NuGetVersion?>(version => version != null && version.ToNormalizedString() == "3.13.0"),
            "https://example.test/v3/index.json",
            includePrerelease: null,
            exact: true,
            force: false,
            progress: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_NoProfileRangeInstallsLatestAndReturnsAlreadyInstalledWorker()
    {
        var workerId = new FunctionsWorkerId(WorkerRuntime);
        _workloadInstaller.InstallFromCatalogAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(CreateWorkerEntry("3.14.0"), AlreadyInstalled: true));
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        FunctionsWorkerInstallResult result = await installer.InstallAsync(workerId, new Dictionary<string, VersionRange>(), CancellationToken.None);

        result.WorkloadInstallResult.AlreadyInstalled.Should().BeTrue();
        result.Worker.Version.Should().Be("3.14.0");
        await _workloadInstaller.Received(1).InstallFromCatalogAsync(
            WorkerPackageId,
            version: null,
            source: null,
            includePrerelease: null,
            exact: true,
            force: false,
            progress: null,
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallAsync_NoPackageInProfileRange_ThrowsWorkloadPackageNotFound()
    {
        var workerId = new FunctionsWorkerId(WorkerRuntime);
        var range = VersionRange.Parse("[3.13.0]");
        Dictionary<string, VersionRange> workerRanges = new(StringComparer.OrdinalIgnoreCase)
        {
            [WorkerRuntime] = range,
        };
        _workloadCatalog.ResolveLatestVersionInRangeAsync(
                WorkerPackageId,
                range,
                includePrerelease: null,
                source: null,
                Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        WorkloadPackageNotFoundException exception = (await FluentActions.Awaiting(async () => await installer.InstallAsync(workerId, workerRanges, CancellationToken.None)).Should().ThrowAsync<WorkloadPackageNotFoundException>()).Which;

        exception.Message.Should().Contain(WorkerPackageId);
        exception.Message.Should().Contain("[3.13.0]");
    }

    [Fact]
    public async Task InstallAsync_InstalledPackageIsNotContent_ThrowsInvalidWorkload()
    {
        var workerId = new FunctionsWorkerId(WorkerRuntime);
        WorkloadEntry entry = CreateWorkerEntry("3.13.0", WorkloadKind.Workload);
        _workloadInstaller.InstallFromCatalogAsync(
                Arg.Any<string>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<string?>(),
                Arg.Any<bool?>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<IProgress<WorkloadInstallProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadInstallResult(entry, AlreadyInstalled: false));
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        InvalidWorkloadException exception = (await FluentActions.Awaiting(async () => await installer.InstallAsync(workerId, new Dictionary<string, VersionRange>(), CancellationToken.None)).Should().ThrowAsync<InvalidWorkloadException>()).Which;

        exception.Message.Should().Contain("kind 'content'");
    }

    [Fact]
    public async Task InstallAsync_NullWorkerId_Throws()
    {
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        await FluentActions.Awaiting(async () => await installer.InstallAsync(null!, new Dictionary<string, VersionRange>(), CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task InstallAsync_NullWorkerVersionRanges_Throws()
    {
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        await FluentActions.Awaiting(async () => await installer.InstallAsync(new FunctionsWorkerId(WorkerRuntime), null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_NullDependencies_Throw()
    {
        var contentResolver = new DefaultFunctionsWorkerContentResolver(_fileSystem, Options.Create(new WorkloadCatalogOptions()));

        FluentActions.Invoking(() => new DefaultFunctionsWorkerInstaller(null!, _workloadInstaller, _workloadPaths, contentResolver)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new DefaultFunctionsWorkerInstaller(_workloadCatalog, null!, _workloadPaths, contentResolver)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new DefaultFunctionsWorkerInstaller(_workloadCatalog, _workloadInstaller, null!, contentResolver)).Should().ThrowExactly<ArgumentNullException>();
        FluentActions.Invoking(() => new DefaultFunctionsWorkerInstaller(_workloadCatalog, _workloadInstaller, _workloadPaths, null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    private DefaultFunctionsWorkerInstaller CreateInstaller()
    {
        var contentResolver = new DefaultFunctionsWorkerContentResolver(_fileSystem, Options.Create(new WorkloadCatalogOptions()));
        return new DefaultFunctionsWorkerInstaller(_workloadCatalog, _workloadInstaller, _workloadPaths, contentResolver);
    }

    private static WorkloadEntry CreateWorkerEntry(string packageVersion, WorkloadKind kind = WorkloadKind.Content)
        => new()
        {
            PackageId = WorkerPackageId,
            PackageVersion = packageVersion,
            Kind = kind,
            Aliases = ["node-worker"],
            DisplayName = WorkerPackageId,
            Description = string.Empty,
        };

    private static string GetInstallDirectory(string packageVersion) => Path.Combine(Path.GetTempPath(), "workloads", WorkerPackageId, packageVersion);
}
