// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadInstallerUpdateTests : WorkloadInstallerTestBase
{
    private readonly string _root;
    private readonly IWorkloadStore _store;
    private readonly IWorkloadCatalog _catalog;
    private readonly IWorkloadPaths _paths;

    public WorkloadInstallerUpdateTests()
    {
        _root = Root;
        _store = Store;
        _catalog = Catalog;
        _paths = Paths;
    }

    [Fact]
    public async Task UpdateAsync_NotInstalled_Throws()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.UpdateAsync(
            "test.workload", null, null, false, allowMajor: false);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("*not installed*");
        await _catalog.DidNotReceive().ResolveLatestVersionAsync(
            Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<NuGetVersion?>(),
            Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NoUpdateAvailable_ReturnsFlag_RegistryUntouched()
    {
        WorkloadEntry current = ExistingEntry("test.workload", "1.0.0");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
        _catalog.ResolveLatestVersionAsync(
                "test.workload", false, Arg.Any<NuGetVersion?>(), false, null, Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);

        WorkloadInstaller installer = NewInstaller();
        WorkloadUpdateResult result = await installer.UpdateAsync(
            "test.workload", null, null, false, allowMajor: false);

        result.NoUpdateAvailable.Should().BeTrue();
        result.NoCandidateOnSource.Should().BeTrue();
        result.PreviousVersion.Should().Be("1.0.0");
        result.Entry.Should().BeSameAs(current);
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().RemoveWorkloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_CatalogReturnsOlderVersion_NoUpdateButNotMissing()
    {
        WorkloadEntry current = ExistingEntry("test.workload", "1.5.0");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
        _catalog.ResolveLatestVersionAsync(
                "test.workload", false, Arg.Any<NuGetVersion?>(), false, null, Arg.Any<CancellationToken>())
            .Returns(NewResolved("test.workload", "1.5.0"));

        WorkloadInstaller installer = NewInstaller();
        WorkloadUpdateResult result = await installer.UpdateAsync(
            "test.workload", null, null, false, allowMajor: false);

        result.NoUpdateAvailable.Should().BeTrue();
        result.NoCandidateOnSource.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_HappyPath_SwapsRegistryAndDeletesOldDir()
    {
        WorkloadEntry current = ExistingEntry("test.workload", "1.0.0");
        string oldDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "marker.txt"), "old");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);

        string newNupkg = BuildNupkg(version: "1.1.0");
        var resolved = NewResolved("test.workload", "1.1.0");
        _catalog.ResolveLatestVersionAsync(
                "test.workload", false, Arg.Any<NuGetVersion?>(), false, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(newNupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadUpdateResult result = await installer.UpdateAsync(
            "test.workload", null, null, false, allowMajor: false);

        result.NoUpdateAvailable.Should().BeFalse();
        result.PreviousVersion.Should().Be("1.0.0");
        result.Entry.PackageVersion.Should().Be("1.1.0");

        string newDir = _paths.GetInstallDirectory("test.workload", "1.1.0");
        Directory.Exists(newDir).Should().BeTrue();
        Directory.Exists(oldDir).Should().BeFalse("old install dir must be deleted after swap");
        await _store.Received(1).MoveExplicitOwnershipAsync(
            "test.workload",
            "1.0.0",
            Arg.Is<WorkloadEntry>(entry =>
                entry.PackageId == "test.workload"
                && entry.PackageVersion == "1.1.0"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StagingFails_LeavesExistingInstallIntact()
    {
        WorkloadEntry current = ExistingEntry("test.workload", "1.0.0");
        string oldDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "marker.txt"), "old");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);

        var resolved = NewResolved("test.workload", "1.1.0");
        _catalog.ResolveLatestVersionAsync(
                "test.workload", false, Arg.Any<NuGetVersion?>(), false, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("network glitch"));
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.UpdateAsync(
            "test.workload", null, null, false, allowMajor: false);

        await act.Should().ThrowExactlyAsync<IOException>();
        Directory.Exists(oldDir).Should().BeTrue("existing install must remain after staging failure");
        File.Exists(Path.Combine(oldDir, "marker.txt")).Should().BeTrue();
        await _store.DidNotReceive().MoveExplicitOwnershipAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RequestedVersionNotInstalled_Throws()
    {
        WorkloadEntry current = ExistingEntry("test.workload", "1.0.0");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.UpdateAsync(
            "test.workload", NuGetVersion.Parse("0.9.0"), null, false, allowMajor: false);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("*0.9.0*not installed*");
    }

    [Fact]
    public async Task UpdateLogical_LocalPointerSourceMissing_RequiresExplicitSource()
    {
        const string pointerId = "example.workload";
        string missingPointerPath = Path.Combine(_root, "deleted-pointer.nupkg");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry
            {
                PackageId = "example.workload.win-x64",
                PackageVersion = "1.0.0",
                RuntimeIdentifier = "win-x64",
                IsExplicitlyInstalled = false,
                LogicalPackage = new LogicalPackage
                {
                    PackageId = pointerId,
                    PackageVersion = "1.0.0",
                    Source = missingPointerPath,
                },
            },
        ]);
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());

        Func<Task> act = () => installer.UpdateAsync(
            pointerId, targetInstalledVersion: null, source: null,
            includePrerelease: false, allowMajor: false, WorkloadOwnershipKind.Logical);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>()
            .WithMessage("*local package*");
        await _catalog.DidNotReceive().ResolveLatestVersionAsync(
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<NuGetVersion?>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateLogical_WithoutVersionTargetsHighestInstalledPointerVersion()
    {
        const string pointerId = "example.workload";
        var source = new PackageSource("https://example.test/v3/index.json", "test");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            LogicalEntry("1.0.0"),
            LogicalEntry("2.0.0"),
        ]);
        _catalog.ResolveLatestVersionAsync(
                pointerId,
                false,
                NuGetVersion.Parse("2.0.0"),
                false,
                source.Source,
                Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);

        WorkloadInstaller installer = NewInstaller();
        WorkloadUpdateResult result = await installer.UpdateAsync(
            pointerId, targetInstalledVersion: null, source: null,
            includePrerelease: false, allowMajor: false, WorkloadOwnershipKind.Logical);

        result.PreviousVersion.Should().Be("2.0.0");

        WorkloadEntry LogicalEntry(string version) => new()
        {
            PackageId = $"example.workload.win-x64.{version}",
            PackageVersion = version,
            RuntimeIdentifier = "win-x64",
            Source = source.Source,
            IsExplicitlyInstalled = false,
            LogicalPackage = new LogicalPackage
            {
                PackageId = pointerId,
                PackageVersion = version,
                Source = source.Source,
            },
        };
    }

    [Fact]
    public async Task UpdateLogical_OlderPointerVersionDoesNotDowngradeForRidChange()
    {
        const string pointerId = "example.workload";
        WorkloadEntry current = new()
        {
            PackageId = "example.workload.incompatible-rid",
            PackageVersion = "2.0.0",
            RuntimeIdentifier = "incompatible-rid",
            Source = "https://example.test/v3/index.json",
            IsExplicitlyInstalled = false,
            LogicalPackage = new LogicalPackage
            {
                PackageId = pointerId,
                PackageVersion = "2.0.0",
                Source = "https://example.test/v3/index.json",
            },
        };
        ResolvedPackage olderPointer = NewResolved(pointerId, "1.0.0");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);
        _catalog.ResolveLatestVersionAsync(
                pointerId,
                false,
                NuGetVersion.Parse("2.0.0"),
                false,
                current.Source,
                Arg.Any<CancellationToken>())
            .Returns(olderPointer);

        WorkloadInstaller installer = NewInstaller();
        WorkloadUpdateResult result = await installer.UpdateAsync(
            pointerId, targetInstalledVersion: null, source: null,
            includePrerelease: false, allowMajor: false, WorkloadOwnershipKind.Logical);

        result.NoUpdateAvailable.Should().BeTrue();
        result.Entry.PackageVersion.Should().Be("2.0.0");
        await _catalog.DidNotReceive().DownloadAsync(
            Arg.Any<ResolvedPackage>(),
            Arg.Any<CancellationToken>());
    }
}
