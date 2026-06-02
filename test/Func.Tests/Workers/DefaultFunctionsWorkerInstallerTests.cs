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
using Xunit;

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

        Assert.Equal(WorkerRuntime, result.Worker.WorkerRuntime);
        Assert.Equal("3.13.0", result.Worker.Version);
        Assert.False(result.WorkloadInstallResult.AlreadyInstalled);
        Assert.Equal(
            Path.Combine(GetInstallDirectory("3.13.0"), "tools", "any", "worker.config.json"),
            result.Worker.WorkerConfigPath);
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

        Assert.True(result.WorkloadInstallResult.AlreadyInstalled);
        Assert.Equal("3.14.0", result.Worker.Version);
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

        WorkloadPackageNotFoundException exception = await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            async () => await installer.InstallAsync(workerId, workerRanges, CancellationToken.None));

        Assert.Contains(WorkerPackageId, exception.Message);
        Assert.Contains("[3.13.0]", exception.Message);
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

        InvalidWorkloadException exception = await Assert.ThrowsAsync<InvalidWorkloadException>(
            async () => await installer.InstallAsync(workerId, new Dictionary<string, VersionRange>(), CancellationToken.None));

        Assert.Contains("kind 'content'", exception.Message);
    }

    [Fact]
    public async Task InstallAsync_NullWorkerId_Throws()
    {
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await installer.InstallAsync(null!, new Dictionary<string, VersionRange>(), CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_NullWorkerVersionRanges_Throws()
    {
        DefaultFunctionsWorkerInstaller installer = CreateInstaller();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await installer.InstallAsync(new FunctionsWorkerId(WorkerRuntime), null!, CancellationToken.None));
    }

    [Fact]
    public void Ctor_NullDependencies_Throw()
    {
        var contentResolver = new DefaultFunctionsWorkerContentResolver(_fileSystem, Options.Create(new WorkloadCatalogOptions()));

        Assert.Throws<ArgumentNullException>(() => new DefaultFunctionsWorkerInstaller(null!, _workloadInstaller, _workloadPaths, contentResolver));
        Assert.Throws<ArgumentNullException>(() => new DefaultFunctionsWorkerInstaller(_workloadCatalog, null!, _workloadPaths, contentResolver));
        Assert.Throws<ArgumentNullException>(() => new DefaultFunctionsWorkerInstaller(_workloadCatalog, _workloadInstaller, null!, contentResolver));
        Assert.Throws<ArgumentNullException>(() => new DefaultFunctionsWorkerInstaller(_workloadCatalog, _workloadInstaller, _workloadPaths, null!));
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
