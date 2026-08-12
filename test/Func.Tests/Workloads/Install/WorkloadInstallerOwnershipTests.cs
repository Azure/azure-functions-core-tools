// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadInstallerOwnershipTests : WorkloadInstallerTestBase
{
    private readonly IWorkloadStore _store;
    private readonly IWorkloadCatalog _catalog;
    private readonly IWorkloadPaths _paths;

    public WorkloadInstallerOwnershipTests()
    {
        _store = Store;
        _catalog = Catalog;
        _paths = Paths;
    }

    [Fact]
    public async Task InstallFromPackage_AlreadyInRegistry_AndOnDisk_IsNoOp()
    {
        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(installDir);
        WorkloadEntry priorEntry = new()
        {
            PackageId = "test.workload",
            PackageVersion = "1.0.0",
            Aliases = ["stub"],
            Kind = WorkloadKind.Workload,
            EntryPoint = new EntryPointSpec { AssemblyPath = "Test.dll", Type = "Test.Type" },
        };
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([priorEntry]);
        string nupkg = BuildNupkg();

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.AlreadyInstalled.Should().BeTrue();
        result.Entry.Should().BeSameAs(priorEntry);
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().RemoveWorkloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_AlreadyInstalled_DirOnly_NoRegistryEntry_SelfHealsAndInstalls()
    {
        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(installDir);
        string stalePath = Path.Combine(installDir, "stale.txt");
        File.WriteAllText(stalePath, "leftover from blocked uninstall");
        string nupkg = BuildNupkg();

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.AlreadyInstalled.Should().BeFalse();
        File.Exists(stalePath).Should().BeFalse();
        await _store.Received(1).SaveWorkloadAsync(
            Arg.Is<WorkloadEntry>(entry =>
                entry.PackageId == "test.workload"
                && entry.PackageVersion == "1.0.0"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_AlreadyInstalled_Force_ReplacesInstall()
    {
        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(installDir);
        string stalePath = Path.Combine(installDir, "stale.txt");
        File.WriteAllText(stalePath, "leftover from prior install");
        string nupkg = BuildNupkg();
        _store.RemoveOwnershipAsync(
                "test.workload",
                "1.0.0",
                WorkloadOwnershipKind.Explicit,
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(true, true, null));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg, force: true);

        result.AlreadyInstalled.Should().BeFalse();
        result.Entry.PackageId.Should().Be("test.workload");
        File.Exists(stalePath).Should().BeFalse("stale files from the prior install must be removed");
        File.Exists(Path.Combine(installDir, "tools", "any", "Test.dll")).Should().BeTrue();
        await _store.Received(1).SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_StoreFails_RollsBackInstallDir()
    {
        string nupkg = BuildNupkg();
        _store.SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("disk full"));
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.InstallFromPackageAsync(nupkg);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")).Should().BeFalse();
    }

    [Fact]
    public async Task InstallFromPackage_ForcedReinstallStoreFails_KeepsReplacementPayload()
    {
        WorkloadEntry existing = ExistingEntry("test.workload", "1.0.0");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([existing]);
        _store.SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("disk full"));
        string installDir = _paths.GetInstallDirectory(existing.PackageId, existing.PackageVersion);
        Directory.CreateDirectory(installDir);
        string stalePath = Path.Combine(installDir, "stale.txt");
        File.WriteAllText(stalePath, "old payload");
        string nupkg = BuildNupkg();
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.InstallFromPackageAsync(nupkg, force: true);

        await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        Directory.Exists(installDir).Should().BeTrue();
        File.Exists(stalePath).Should().BeFalse();
        File.Exists(Path.Combine(installDir, "tools", "any", "Test.dll")).Should().BeTrue();
    }

    [Fact]
    public async Task Uninstall_RemovesEntryAndDirectory()
    {
        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "Test.dll"), "stub");
        _store.RemoveOwnershipAsync(
                "test.workload",
                "1.0.0",
                WorkloadOwnershipKind.Explicit,
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(true, true, null));

        WorkloadInstaller installer = NewInstaller();
        bool removed = await installer.UninstallAsync("test.workload", "1.0.0");

        removed.Should().BeTrue();
        Directory.Exists(installDir).Should().BeFalse();
    }

    [Fact]
    public async Task Uninstall_NoSuchEntry_ReturnsFalse_LeavesDirectoryAlone()
    {
        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(installDir);
        WorkloadInstaller installer = NewInstaller();

        bool removed = await installer.UninstallAsync("test.workload", "1.0.0");

        removed.Should().BeFalse();
        Directory.Exists(installDir).Should().BeTrue();
    }

    [Fact]
    public async Task Uninstall_LogicalOwnership_ResolvesPhysicalRegistryRow()
    {
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
                    PackageId = "example.workload",
                    PackageVersion = "1.0.0",
                },
            },
        ]);
        _store.RemoveOwnershipAsync(
                "example.workload.win-x64",
                "1.0.0",
                WorkloadOwnershipKind.Logical,
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(true, false, null));

        WorkloadInstaller installer = NewInstaller();
        bool removed = await installer.UninstallAsync(
            "example.workload", "1.0.0", WorkloadOwnershipKind.Logical);

        removed.Should().BeTrue();
        await _store.Received(1).RemoveOwnershipAsync(
            "example.workload.win-x64",
            "1.0.0",
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_PointerReusingInstalledImplementation_MovesLogicalOwnershipOffOtherRid()
    {
        const string pointerId = "example.workload";
        const string implementationId = "example.workload.win-x64";
        const string otherImplementationId = "example.workload.linux-x64";
        const string version = "2.3.4";
        string pointerPath = BuildPointerNupkg(
            pointerId,
            version,
            """{ "win-x64": "example.workload.win-x64" }""");
        var source = new PackageSource("https://example.test/v3/index.json", "pointer-source");
        var pointerResolved = new ResolvedPackage(pointerId, NuGetVersion.Parse(version), source);
        InstallImplementationOnDisk(implementationId, version, "win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry
            {
                PackageId = implementationId,
                PackageVersion = version,
                RuntimeIdentifier = "win-x64",
                Source = source.Source,
                IsExplicitlyInstalled = true,
            },
            new WorkloadEntry
            {
                PackageId = otherImplementationId,
                PackageVersion = version,
                RuntimeIdentifier = "linux-x64",
                Source = source.Source,
                IsExplicitlyInstalled = false,
                LogicalPackage = new LogicalPackage
                {
                    PackageId = pointerId,
                    PackageVersion = version,
                    Source = source.Source,
                },
            },
        ]);
        _catalog.ResolveLatestVersionAsync(
                pointerId,
                Arg.Any<bool>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(pointerResolved);
        _catalog.DownloadAsync(pointerResolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            pointerId, version: null, source: null, includePrerelease: false, exact: true, force: false);

        result.Entry.PackageId.Should().Be(implementationId);
        await _store.Received(1).MoveLogicalOwnershipAsync(
            otherImplementationId,
            version,
            Arg.Is<WorkloadEntry>(entry =>
                entry.PackageId == implementationId
                && entry.LogicalPackage!.PackageId == pointerId),
            Arg.Any<CancellationToken>());
        await _store.DidNotReceive().AddOwnershipAsync(
            Arg.Any<WorkloadEntry>(),
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_PointerReusesPayloadInstalledFromAnotherSource()
    {
        const string pointerId = "example.workload";
        const string implementationId = "example.workload.win-x64";
        const string version = "2.3.4";
        string pointerPath = BuildPointerNupkg(
            pointerId,
            version,
            """{ "win-x64": "example.workload.win-x64" }""");
        var pointerSource = new PackageSource("https://pointer.test/v3/index.json", "pointer-source");
        var pointerResolved = new ResolvedPackage(pointerId, NuGetVersion.Parse(version), pointerSource);
        InstallImplementationOnDisk(implementationId, version, "win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry
            {
                PackageId = implementationId,
                PackageVersion = version,
                RuntimeIdentifier = "win-x64",
                Source = "https://other.test/v3/index.json",
                IsExplicitlyInstalled = true,
            },
        ]);
        _catalog.ResolveLatestVersionAsync(
                pointerId,
                Arg.Any<bool>(),
                Arg.Any<NuGetVersion?>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(pointerResolved);
        _catalog.DownloadAsync(pointerResolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            pointerId, version: null, source: null, includePrerelease: false, exact: true, force: false);

        result.Entry.PackageId.Should().Be(implementationId);
        await _store.Received(1).AddOwnershipAsync(
            Arg.Is<WorkloadEntry>(entry => entry.LogicalPackage!.PackageId == pointerId),
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
        await _catalog.DidNotReceive().ResolveVersionAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_ForcedInstall_DoesNotReuseInstalledImplementation()
    {
        const string pointerId = "example.workload";
        const string implementationId = "example.workload.win-x64";
        const string version = "1.0.0";
        string pointerPath = BuildPointerNupkg(
            pointerId,
            version,
            """{ "win-x64": "example.workload.win-x64" }""");
        var pointerSource = new PackageSource("https://pointer.test/v3/index.json", "pointer");
        var pointerResolved = new ResolvedPackage(pointerId, NuGetVersion.Parse(version), pointerSource);
        string installDirectory = _paths.GetInstallDirectory(implementationId, version);
        Directory.CreateDirectory(installDirectory);
        File.WriteAllText(
            Path.Combine(installDirectory, "workload.json"),
            """
            {
              "$schema": "https://aka.ms/func-workloads/package/v1/schema.json",
              "kind": "content",
              "runtimeIdentifier": "win-x64"
            }
            """);
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry
            {
                PackageId = implementationId,
                PackageVersion = version,
                Kind = WorkloadKind.Content,
                RuntimeIdentifier = "win-x64",
                Source = pointerSource.Source,
                IsExplicitlyInstalled = true,
            },
        ]);
        _catalog.ResolveLatestVersionAsync(
                pointerId,
                false,
                null,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(pointerResolved);
        _catalog.DownloadAsync(pointerResolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));
        _catalog.ResolveVersionAsync(
                implementationId,
                NuGetVersion.Parse(version),
                pointerSource.Source,
                Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            pointerId, version: null, source: null, includePrerelease: false, exact: true, force: true);

        await act.Should().ThrowExactlyAsync<WorkloadPackageNotFoundException>();
        await _catalog.Received(1).ResolveVersionAsync(
            implementationId,
            NuGetVersion.Parse(version),
            pointerSource.Source,
            Arg.Any<CancellationToken>());
        await _store.DidNotReceive().AddOwnershipAsync(
            Arg.Any<WorkloadEntry>(),
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_LocalPointerRidChange_MovesLogicalOwnership()
    {
        const string pointerId = "example.workload";
        const string version = "1.0.0";
        string pointerPath = BuildPointerNupkg(
            pointerId,
            version,
            """{ "win-x64": "example.workload.win-x64" }""");
        _ = BuildRidImplementationNupkg("example.workload.win-x64", version, "win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry
            {
                PackageId = "example.workload.linux-x64",
                PackageVersion = version,
                RuntimeIdentifier = "linux-x64",
                IsExplicitlyInstalled = false,
                LogicalPackage = new LogicalPackage
                {
                    PackageId = pointerId,
                    PackageVersion = version,
                    Source = pointerPath,
                },
            },
        ]);

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(pointerPath);

        result.Entry.PackageId.Should().Be("example.workload.win-x64");
        await _store.Received(1).MoveLogicalOwnershipAsync(
            "example.workload.linux-x64",
            version,
            Arg.Is<WorkloadEntry>(entry =>
                entry.PackageId == "example.workload.win-x64"
                && entry.LogicalPackage!.PackageId == pointerId),
            Arg.Any<CancellationToken>());
        await _store.DidNotReceive().SaveWorkloadAsync(
            Arg.Any<WorkloadEntry>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_LocalPointer_ReusesPhysicalPackageFromDifferentSource()
    {
        const string pointerId = "example.workload";
        const string implementationId = "example.workload.win-x64";
        const string version = "1.0.0";
        string pointerPath = BuildPointerNupkg(
            pointerId,
            version,
            """{ "win-x64": "example.workload.win-x64" }""");
        _ = BuildRidImplementationNupkg(implementationId, version, "win-x64");
        InstallImplementationOnDisk(implementationId, version, "win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WorkloadEntry
            {
                PackageId = implementationId,
                PackageVersion = version,
                RuntimeIdentifier = "win-x64",
                Source = "https://different.test/v3/index.json",
                IsExplicitlyInstalled = true,
            },
        ]);

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(pointerPath);

        result.Entry.PackageId.Should().Be(implementationId);
        await _store.Received(1).AddOwnershipAsync(
            Arg.Is<WorkloadEntry>(entry => entry.LogicalPackage!.PackageId == pointerId),
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
    }
}
