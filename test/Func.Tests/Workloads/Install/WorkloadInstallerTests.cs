// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("workload-installer-").FullName;
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadMetadataReader _metadataReader = Substitute.For<IWorkloadMetadataReader>();
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadRuntimeIdentifierProvider _runtimeIdentifierProvider = Substitute.For<IWorkloadRuntimeIdentifierProvider>();
    private readonly IWorkloadPaths _paths;

    public WorkloadInstallerTests()
    {
        _paths = new WorkloadPathsOptions(Path.Combine(_root, ".azure-functions"));
        _metadataReader.Read(Arg.Any<string>())
            .Returns(new WorkloadMetadata
            {
                Schema = "https://example/workload.schema.json",
                EntryPoint = new EntryPointSpec { AssemblyPath = "Test.dll", Type = "Test.Type" },
            });
        _runtimeIdentifierProvider.Current.Returns("win-x64");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _store.AddOwnershipAsync(
                Arg.Any<WorkloadEntry>(),
                Arg.Any<WorkloadOwnershipKind>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<WorkloadEntry>());
        _store.MoveExplicitOwnershipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WorkloadEntry>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new WorkloadOwnershipMoveResult(call.Arg<WorkloadEntry>(), PreviousEntryRemoved: true));
        _store.MoveLogicalOwnershipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WorkloadEntry>(),
                Arg.Any<CancellationToken>())
            .Returns(call => new WorkloadOwnershipMoveResult(call.Arg<WorkloadEntry>(), PreviousEntryRemoved: true));
        _store.RemoveOwnershipAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WorkloadOwnershipKind>(),
                Arg.Any<CancellationToken>())
            .Returns(new WorkloadOwnershipRemovalResult(false, false, null));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task InstallFromPackage_HappyPath_ExtractsAndPersists()
    {
        string nupkg = BuildNupkg(tags: $"{WorkloadInstaller.AliasTagPrefix}test {WorkloadInstaller.AliasTagPrefix}stub other-tag");

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.AlreadyInstalled.Should().BeFalse();
        result.Entry.PackageId.Should().Be("test.workload");
        result.Entry.PackageVersion.Should().Be("1.0.0");
        result.Entry.Aliases.Should().Equal(["test", "stub"]);
        result.Entry.EntryPoint!.AssemblyPath.Should().Be("Test.dll");
        result.Entry.Source.Should().Be(Path.GetFullPath(nupkg));
        result.Entry.InstallRefCount.Should().Be(1);

        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.Exists(installDir).Should().BeTrue();
        File.Exists(Path.Combine(installDir, "tools", "any", "Test.dll")).Should().BeTrue();
        File.Exists(nupkg).Should().BeTrue("Source .nupkg must be left in place.");

        await _store.Received(1).SaveWorkloadAsync(
            Arg.Is<WorkloadEntry>(e =>
                e.PackageId == "test.workload" &&
                e.PackageVersion == "1.0.0" &&
                e.EntryPoint!.AssemblyPath == "Test.dll" &&
                e.DisplayName == "test.workload" &&
                e.Description == "For tests." &&
                e.Source == Path.GetFullPath(nupkg) &&
                e.InstallRefCount == 1 &&
                e.Aliases.SequenceEqual(new[] { "test", "stub" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_NonAliasTagsIgnored()
    {
        string nupkg = BuildNupkg(tags: "search-keyword another-keyword");

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.Entry.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task InstallFromPackage_OnlyExtractsWorkloadJsonAndTools()
    {
        // Real workload packages also carry pack-time metadata (the .nuspec,
        // icons, docs) that has no business landing in the install dir.
        string nupkg = BuildNupkg(extraFiles:
        [
            (WriteTempFile("workload.json", "{}"), "workload.json"),
            (WriteTempFile("readme.md", "# readme"), "readme.md"),
            (WriteTempFile("icon.png", "png"), "icon.png"),
            (WriteTempFile("notes.txt", "notes"), "docs/notes.txt"),
        ]);

        WorkloadInstaller installer = NewInstaller();
        await installer.InstallFromPackageAsync(nupkg);

        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        string[] entries = [.. Directory
            .EnumerateFileSystemEntries(installDir, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(installDir, p).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(p => p, StringComparer.Ordinal)];

        entries.Should().Equal(["tools", "tools/any", "tools/any/Test.dll", "workload.json"]);
    }

    [Fact]
    public async Task InstallFromPackage_InvalidWorkloadJson_Throws_RollsBack()
    {
        string nupkg = BuildNupkg();
        _metadataReader.Read(Arg.Any<string>())
            .Returns(_ => throw new InvalidWorkloadException("missing workload.json"));

        WorkloadInstaller installer = NewInstaller();
        InvalidWorkloadException ex = (await FluentActions.Awaiting(() => installer.InstallFromPackageAsync(nupkg)).Should().ThrowAsync<InvalidWorkloadException>()).Which;

        ex.Message.Should().Contain("missing workload.json");
        Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")).Should().BeFalse();
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_MissingFuncCliWorkloadPackageType_Throws_NoExtraction()
    {
        string nupkg = BuildNupkg(includeFuncCliWorkloadType: false);

        WorkloadInstaller installer = NewInstaller();
        InvalidWorkloadException ex = (await FluentActions.Awaiting(() => installer.InstallFromPackageAsync(nupkg)).Should().ThrowAsync<InvalidWorkloadException>()).Which;

        ex.Message.Should().Contain("FuncCliWorkload");
        Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")).Should().BeFalse();
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_MissingFile_Throws()
    {
        WorkloadInstaller installer = NewInstaller();
        FileNotFoundException ex = (await FluentActions.Awaiting(() => installer.InstallFromPackageAsync(Path.Combine(_root, "missing.nupkg"))).Should().ThrowAsync<FileNotFoundException>()).Which;
        ex.Message.Should().Contain("does not exist");
    }

    [Fact]
    public async Task InstallFromPackage_AlreadyInRegistry_AndOnDisk_IsNoOp()
    {
        // Spec §6.1 step 0: same (id, version) already present and intact →
        // exit success without re-extracting. The pre-existing registry
        // entry is returned verbatim and SaveWorkloadAsync is not called
        // again.
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
        // Orphaned directory (registry doesn't know about it, e.g. a prior
        // uninstall whose Directory.Delete was blocked by AV) used to block
        // reinstall. The installer now treats it as stale, wipes it, and
        // extracts fresh so the user can recover without manual cleanup.
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
            Arg.Is<WorkloadEntry>(e => e.PackageId == "test.workload" && e.PackageVersion == "1.0.0"),
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
        File.Exists(stalePath).Should().BeFalse("Stale files from the prior install must be gone after a forced reinstall.");
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
        await FluentActions.Awaiting(() => installer.InstallFromPackageAsync(nupkg)).Should().ThrowAsync<InvalidOperationException>();

        Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")).Should().BeFalse();
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
            "example.workload",
            "1.0.0",
            WorkloadOwnershipKind.Logical);

        Assert.True(removed);
        await _store.Received(1).RemoveOwnershipAsync(
            "example.workload.win-x64",
            "1.0.0",
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_ContentOnly_PersistsContentKind()
    {
        string nupkg = BuildNupkg();
        _metadataReader.Read(Arg.Any<string>())
            .Returns(new WorkloadMetadata { Schema = "https://example/workload.schema.json", Kind = WorkloadKind.Content });

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.Entry.Kind.Should().Be(WorkloadKind.Content);
        result.Entry.EntryPoint.Should().BeNull();
        result.Entry.DisplayName.Should().Be("test.workload");
        result.Entry.Description.Should().Be("For tests.");

        await _store.Received(1).SaveWorkloadAsync(
            Arg.Is<WorkloadEntry>(e =>
                e.Kind == WorkloadKind.Content &&
                e.EntryPoint == null &&
                e.DisplayName == "test.workload" &&
                e.Description == "For tests."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_ResolvesAndInstalls()
    {
        string nupkg = BuildNupkg();
        var resolved = NewResolved("test.workload", "1.0.0");
        _catalog.ResolveLatestVersionAsync(
                "test.workload", false, null, true, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "test.workload", version: null, source: null,
            includePrerelease: false, exact: true, force: false);

        result.AlreadyInstalled.Should().BeFalse();
        result.Entry.PackageId.Should().Be("test.workload");
        result.Entry.PackageVersion.Should().Be("1.0.0");
        Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")).Should().BeTrue();
        await _store.Received(1).SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_NoCandidate_Throws()
    {
        _catalog.ResolveLatestVersionAsync(
                "test.workload", false, null, true, null, Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);

        WorkloadInstaller installer = NewInstaller();
        WorkloadPackageNotFoundException ex = (await FluentActions.Awaiting(() => installer.InstallFromCatalogAsync(
                "test.workload", version: null, source: null,
                includePrerelease: false, exact: true, force: false)).Should().ThrowAsync<WorkloadPackageNotFoundException>()).Which;

        ex.Message.Should().Contain("test.workload");
        ex.Message.Should().Contain("--prerelease");
        await _catalog.DidNotReceive().DownloadAsync(Arg.Any<ResolvedPackage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_NoCandidateWithPrereleaseOverride_UsesPrereleaseAndOmitsHint()
    {
        _catalog.ResolveLatestVersionAsync(
                "test.workload", true, null, true, null, Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);

        WorkloadInstaller installer = NewInstaller(includePrerelease: true);
        WorkloadPackageNotFoundException ex = (await FluentActions.Awaiting(() => installer.InstallFromCatalogAsync(
                "test.workload", version: null, source: null,
                includePrerelease: null, exact: true, force: false)).Should().ThrowAsync<WorkloadPackageNotFoundException>()).Which;

        ex.Message.Should().Contain("test.workload");
        ex.Message.Should().NotContain("--prerelease");
        await _catalog.Received(1).ResolveLatestVersionAsync(
            "test.workload", true, null, true, null, Arg.Any<CancellationToken>());
        await _catalog.DidNotReceive().DownloadAsync(Arg.Any<ResolvedPackage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_ExplicitVersion_RoutesToExactResolution()
    {
        string nupkg = BuildNupkg();
        var requested = NuGetVersion.Parse("1.0.0");
        var resolved = NewResolved("test.workload", "1.0.0");

        // Explicit version path uses the catalog's exact-version lookup.
        _catalog.ResolveVersionAsync(
                "test.workload", requested, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "test.workload", requested, source: null,
            includePrerelease: false, exact: true, force: false);

        result.Entry.PackageVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task UpdateAsync_NotInstalled_Throws()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);

        WorkloadInstaller installer = NewInstaller();
        InvalidOperationException ex = (await FluentActions.Awaiting(() => installer.UpdateAsync(
                "test.workload", null, null, false, allowMajor: false)).Should().ThrowAsync<InvalidOperationException>()).Which;

        ex.Message.Should().Contain("not installed");
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
        WorkloadUpdateResult result = await installer.UpdateAsync("test.workload", null, null, false, allowMajor: false);

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
        WorkloadUpdateResult result = await installer.UpdateAsync("test.workload", null, null, false, allowMajor: false);

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
        WorkloadUpdateResult result = await installer.UpdateAsync("test.workload", null, null, false, allowMajor: false);

        result.NoUpdateAvailable.Should().BeFalse();
        result.PreviousVersion.Should().Be("1.0.0");
        result.Entry.PackageVersion.Should().Be("1.1.0");

        string newDir = _paths.GetInstallDirectory("test.workload", "1.1.0");
        Directory.Exists(newDir).Should().BeTrue();
        Directory.Exists(oldDir).Should().BeFalse("old install dir must be deleted after swap");

        await _store.Received(1).MoveExplicitOwnershipAsync(
            "test.workload",
            "1.0.0",
            Arg.Is<WorkloadEntry>(e => e.PackageId == "test.workload" && e.PackageVersion == "1.1.0"),
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
        await FluentActions.Awaiting(() => installer.UpdateAsync(
            "test.workload", null, null, false, allowMajor: false)).Should().ThrowAsync<IOException>();

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
        InvalidOperationException ex = (await FluentActions.Awaiting(() => installer.UpdateAsync(
                "test.workload", NuGetVersion.Parse("0.9.0"), null, false, allowMajor: false)).Should().ThrowAsync<InvalidOperationException>()).Which;

        ex.Message.Should().Contain("0.9.0");
        ex.Message.Should().Contain("not installed");
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_FallsBackToBroadSearchWhenTargetedReturnsZero()
    {
        // BaGet and older NuGet feeds tokenize the `q=` term in ways that drop
        // hyphenated aliases (e.g. `node-worker`). When the targeted alias
        // search returns nothing, the installer should retry with an empty
        // filter and match by alias client-side.
        string nupkg = BuildNupkg(id: "real.workload.id");
        var resolved = NewResolved("real.workload.id", "1.0.0");
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(q => q.Filter == "node-worker"),
                Arg.Any<CancellationToken>())
            .Returns([]);
        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(q => q.Filter == null),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogSearchResult>
            {
                new("other.workload", NuGetVersion.Parse("1.0.0"), Title: null, Description: null, Aliases: ["other"], Source: source),
                new("real.workload.id", NuGetVersion.Parse("1.0.0"), Title: null, Description: null, Aliases: ["node-worker"], Source: source),
            });
        _catalog.ResolveLatestVersionAsync(
                "real.workload.id", true, null, true, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "node-worker", version: null, source: null,
            includePrerelease: true, exact: false, force: false);

        result.Entry.PackageId.Should().Be("real.workload.id");
        await _catalog.Received(1).SearchAsync(
            Arg.Is<CatalogSearchQuery>(q => q.Filter == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_SkipsFallbackWhenTargetedHasHits()
    {
        // If the targeted search returns any results (even non-matching),
        // we trust the server filter and don't pay for a second broad query.
        string nupkg = BuildNupkg(id: "node.pkg");
        var resolved = NewResolved("node.pkg", "1.0.0");
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(q => q.Filter == "node"),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogSearchResult>
            {
                new("node.pkg", NuGetVersion.Parse("1.0.0"), Title: null, Description: null, Aliases: ["node"], Source: source),
            });
        _catalog.ResolveLatestVersionAsync(
                "node.pkg", false, null, true, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        _ = await installer.InstallFromCatalogAsync(
            "node", version: null, source: null,
            includePrerelease: false, exact: false, force: false);

        await _catalog.DidNotReceive().SearchAsync(
            Arg.Is<CatalogSearchQuery>(q => q.Filter == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_ReportsExtractAndRegisterPhases()
    {
        string nupkg = BuildNupkg();
        var reports = new List<WorkloadInstallProgress>();
        var progress = new RecordingProgress(reports);

        WorkloadInstaller installer = NewInstaller();
        await installer.InstallFromPackageAsync(nupkg, force: false, progress);

        reports.Should().SatisfyRespectively(r => r.Phase.Should().Be(WorkloadInstallPhase.Extracting), r => r.Phase.Should().Be(WorkloadInstallPhase.Registering));
        reports[0].Description.Should().Contain("test.workload");
        reports[1].Description.Should().Contain("test.workload");
    }

    [Fact]
    public async Task InstallFromPackage_PersistsNuspecTitleAndDescriptionWhenMetadataIsBlank()
    {
        string nupkg = BuildNupkg(title: "Functions Host", description: "Azure Functions host workload.");

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.Entry.DisplayName.Should().Be("Functions Host");
        result.Entry.Description.Should().Be("Azure Functions host workload.");

        await _store.Received(1).SaveWorkloadAsync(
            Arg.Is<WorkloadEntry>(e =>
                e.DisplayName == "Functions Host" &&
                e.Description == "Azure Functions host workload."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_WorkloadJsonMetadataWinsOverNuspec()
    {
        string nupkg = BuildNupkg(title: "Nuspec Title", description: "Nuspec description.");
        _metadataReader.Read(Arg.Any<string>())
            .Returns(new WorkloadMetadata
            {
                Schema = "https://example/workload.schema.json",
                EntryPoint = new EntryPointSpec { AssemblyPath = "Test.dll", Type = "Test.Type" },
                DisplayName = "Manifest Name",
                Description = "Manifest description.",
            });

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.Entry.DisplayName.Should().Be("Manifest Name");
        result.Entry.Description.Should().Be("Manifest description.");
    }

    private sealed class RecordingProgress(List<WorkloadInstallProgress> sink) : IProgress<WorkloadInstallProgress>
    {
        public void Report(WorkloadInstallProgress value) => sink.Add(value);
    }

    [Fact]
    public async Task InstallFromCatalog_LegacyRidAliasesRemainAmbiguous()
    {
        // Legacy per-RID packs carry no pointer package, so an alias spanning several
        // of them stays ambiguous: the current runtime identifier no longer disambiguates.
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(q => q.Filter == "python-worker"),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogSearchResult>
            {
                new("azure.functions.cli.workloads.workers.python.win-x64", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["python-worker"], Source: source) { Rid = "win-x64" },
                new("azure.functions.cli.workloads.workers.python.linux-x64", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["python-worker"], Source: source) { Rid = "linux-x64" },
            });

        WorkloadInstaller installer = NewInstaller();
        await FluentActions.Awaiting(() => installer.InstallFromCatalogAsync(
            "python-worker", version: null, source: null,
            includePrerelease: true, exact: false, force: false)).Should().ThrowAsync<AmbiguousPackageMatchException>();
    }

    [Fact]
    public async Task InstallFromCatalog_LegacyRidAliasesDoNotUseCurrentRidFallback()
    {
        // Legacy per-RID packs are ambiguous regardless of the host runtime identifier:
        // there is no current-RID fallback to silently pick a winner.
        const string ridA = "fake-rid-a";
        const string ridB = "fake-rid-b";
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(q => q.Filter == "python-worker"),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogSearchResult>
            {
                new($"azure.functions.cli.workloads.workers.python.{ridA}", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["python-worker"], Source: source) { Rid = ridA },
                new($"azure.functions.cli.workloads.workers.python.{ridB}", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["python-worker"], Source: source) { Rid = ridB },
            });

        WorkloadInstaller installer = NewInstaller();
        AmbiguousPackageMatchException ex = (await FluentActions.Awaiting(() => installer.InstallFromCatalogAsync(
                "python-worker", version: null, source: null,
                includePrerelease: true, exact: false, force: false)).Should().ThrowAsync<AmbiguousPackageMatchException>()).Which;

        ex.Message.Should().Contain("python-worker");
        ex.Message.Should().Contain("matches multiple packages");
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_StillAmbiguous_WhenMatchesLackRidTag()
    {
        // Two unrelated packages declaring the same alias without any `rid:`
        // tag must throw ambiguity, the same as RID-tagged matches.
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(q => q.Filter == "shared-alias"),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogSearchResult>
            {
                new("pkg.one", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["shared-alias"], Source: source),
                new("pkg.two", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["shared-alias"], Source: source),
            });

        WorkloadInstaller installer = NewInstaller();
        await FluentActions.Awaiting(() => installer.InstallFromCatalogAsync(
                "shared-alias", version: null, source: null,
                includePrerelease: true, exact: false, force: false)).Should().ThrowAsync<AmbiguousPackageMatchException>();
    }

    [Fact]
    public async Task InstallFromCatalog_ExplicitPackageId_BypassesAliasResolution()
    {
        // The user can always install a specific per-RID pack by its full id;
        // alias resolution must not rewrite it. exact:true short-circuits the
        // alias search entirely, but we also cover exact:false: passing a real
        // package id (not an alias) should resolve to that id.
        string explicitId = "Azure.Functions.Cli.Workloads.Workers.Python.win-x64";
        string nupkg = BuildNupkg(
            id: explicitId,
            tags: "rid:win-x64",
            packageType: WorkloadInstaller.FuncCliWorkloadRidPackageType);
        var resolved = NewResolved(explicitId, "1.0.0");
        _metadataReader.Read(Arg.Any<string>())
            .Returns(new WorkloadMetadata
            {
                Schema = "https://example/workload.schema.json",
                Kind = WorkloadKind.Content,
                RuntimeIdentifier = "win-x64",
            });

        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _catalog.ResolveLatestVersionAsync(
                explicitId, true, null, true, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            explicitId, version: null, source: null,
            includePrerelease: true, exact: true, force: false);

        result.Should().NotBeNull();
        await _catalog.Received(1).ResolveLatestVersionAsync(
            explicitId, true, null, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_PointerAliasWinsAndResolvesExactImplementationFromSameSource()
    {
        const string pointerId = "example.workload";
        const string implementationId = "example.workload.win-x64";
        const string version = "2.3.4";
        string pointerPath = BuildPointerNupkg(
            pointerId,
            version,
            """
            {
              "win-x64": "example.workload.win-x64"
            }
            """);
        string implementationPath = BuildRidImplementationNupkg(implementationId, version, "win-x64");
        var source = new PackageSource("https://example.test/v3/index.json", "pointer-source");
        var pointerResolved = new ResolvedPackage(pointerId, NuGetVersion.Parse(version), source);
        var implementationResolved = new ResolvedPackage(implementationId, NuGetVersion.Parse(version), source);

        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new CatalogSearchResult(
                    pointerId,
                    NuGetVersion.Parse(version),
                    "Example",
                    "Example pointer",
                    ["example"],
                    source)
                {
                    Kind = "rid-pointer",
                },
                new CatalogSearchResult(
                    implementationId,
                    NuGetVersion.Parse(version),
                    null,
                    null,
                    ["example"],
                    source)
                {
                    Kind = "content",
                    Rid = "win-x64",
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
        _catalog.ResolveVersionAsync(
                implementationId,
                NuGetVersion.Parse(version),
                source.Source,
                Arg.Any<CancellationToken>())
            .Returns(implementationResolved);
        _catalog.DownloadAsync(pointerResolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));
        _catalog.DownloadAsync(implementationResolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(implementationPath));

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "example",
            version: null,
            source: null,
            includePrerelease: false,
            exact: false,
            force: false);

        Assert.Equal(implementationId, result.Entry.PackageId);
        Assert.Equal("win-x64", result.Entry.RuntimeIdentifier);
        Assert.False(result.Entry.IsExplicitlyInstalled);
        Assert.Equal(pointerId, result.Entry.LogicalPackage!.PackageId);
        Assert.Equal(version, result.Entry.LogicalPackage.PackageVersion);
        await _catalog.Received(1).ResolveVersionAsync(
            implementationId,
            NuGetVersion.Parse(version),
            source.Source,
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

        // The current-RID implementation is already on disk, so the installer takes the reuse fast
        // path while the pointer is still attached to a different RID's payload.
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
            pointerId,
            version: null,
            source: null,
            includePrerelease: false,
            exact: true,
            force: false);

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

        // Installed identity is id + version, as with the NuGet global packages folder, so a payload
        // installed from a different feed is reused rather than rejected.
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
            pointerId,
            version: null,
            source: null,
            includePrerelease: false,
            exact: true,
            force: false);

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
    public async Task InstallFromCatalog_PointerWithoutCurrentRidListsSupportedRids()
    {
        string pointerPath = BuildPointerNupkg(
            "example.workload",
            "1.0.0",
            """
            {
              "linux-x64": "example.workload.linux-x64",
              "osx-arm64": "example.workload.osx-arm64"
            }
            """);
        var resolved = NewResolved("example.workload", "1.0.0");
        _catalog.ResolveLatestVersionAsync(
                "example.workload",
                false,
                null,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadPackageNotFoundException ex = await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => installer.InstallFromCatalogAsync(
                "example.workload",
                version: null,
                source: null,
                includePrerelease: false,
                exact: true,
                force: false));

        Assert.Contains("win-x64", ex.Message);
        Assert.Contains("linux-x64, osx-arm64", ex.Message);
    }

    [Fact]
    public async Task InstallFromCatalog_MissingPointerImplementationReportsPartialPublish()
    {
        string pointerPath = BuildPointerNupkg(
            "example.workload",
            "1.0.0",
            """
            {
              "win-x64": "example.workload.win-x64"
            }
            """);
        var resolved = NewResolved("example.workload", "1.0.0");
        _catalog.ResolveLatestVersionAsync(
                "example.workload",
                false,
                null,
                true,
                null,
                Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));
        _catalog.ResolveVersionAsync(
                "example.workload.win-x64",
                NuGetVersion.Parse("1.0.0"),
                resolved.Source.Source,
                Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadPackageNotFoundException ex = await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => installer.InstallFromCatalogAsync(
                "example.workload",
                version: null,
                source: null,
                includePrerelease: false,
                exact: true,
                force: false));

        Assert.Contains("partial publish", ex.Message);
        Assert.Contains("example.workload.win-x64", ex.Message);
        Assert.Contains(resolved.Source.Source, ex.Message);
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
        _ = await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(() =>
            installer.InstallFromCatalogAsync(
                pointerId,
                version: null,
                source: null,
                includePrerelease: false,
                exact: true,
                force: true));

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
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            installer.UpdateAsync(
                pointerId,
                targetInstalledVersion: null,
                source: null,
                includePrerelease: false,
                allowMajor: false,
                WorkloadOwnershipKind.Logical));

        Assert.Contains("local package", ex.Message);
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
            pointerId,
            targetInstalledVersion: null,
            source: null,
            includePrerelease: false,
            allowMajor: false,
            WorkloadOwnershipKind.Logical);

        Assert.Equal("2.0.0", result.PreviousVersion);

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
            pointerId,
            targetInstalledVersion: null,
            source: null,
            includePrerelease: false,
            allowMajor: false,
            WorkloadOwnershipKind.Logical);

        Assert.True(result.NoUpdateAvailable);
        Assert.Equal("2.0.0", result.Entry.PackageVersion);
        await _catalog.DidNotReceive().DownloadAsync(
            Arg.Any<ResolvedPackage>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_LocalPointerUsesExactSibling()
    {
        string pointerPath = BuildPointerNupkg(
            "example.workload",
            "1.0.0",
            """
            {
              "win-x64": "example.workload.win-x64"
            }
            """);
        _ = BuildRidImplementationNupkg("example.workload.win-x64", "1.0.0", "win-x64");

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(pointerPath);

        Assert.Equal("example.workload.win-x64", result.Entry.PackageId);
        Assert.Equal(Path.GetFullPath(pointerPath), result.Entry.LogicalPackage!.Source);
        await _catalog.DidNotReceive().ResolveVersionAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion>(),
            Arg.Any<string?>(),
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

        Assert.Equal("example.workload.win-x64", result.Entry.PackageId);
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

        Assert.Equal(implementationId, result.Entry.PackageId);
        await _store.Received(1).AddOwnershipAsync(
            Arg.Is<WorkloadEntry>(entry => entry.LogicalPackage!.PackageId == pointerId),
            WorkloadOwnershipKind.Logical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_LocalPointerMissingSiblingDoesNotUseCatalog()
    {
        string pointerPath = BuildPointerNupkg(
            "missing.workload",
            "1.0.0",
            """
            {
              "win-x64": "missing.workload.win-x64"
            }
            """);

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => installer.InstallFromPackageAsync(pointerPath));

        Assert.Contains("missing.workload.win-x64", ex.Message);
        Assert.Contains(Path.GetDirectoryName(pointerPath)!, ex.Message);
        Assert.Contains("No configured feed", ex.Message);
        await _catalog.DidNotReceive().SearchAsync(
            Arg.Any<CatalogSearchQuery>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_RidImplementationWithOrdinaryPackageTypeIsRejected()
    {
        string packagePath = BuildRidImplementationNupkg(
            "example.workload.win-x64",
            "1.0.0",
            "win-x64",
            WorkloadInstaller.FuncCliWorkloadPackageType);

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        InvalidWorkloadException ex = await Assert.ThrowsAsync<InvalidWorkloadException>(
            () => installer.InstallFromPackageAsync(packagePath));

        Assert.Contains(WorkloadInstaller.FuncCliWorkloadRidPackageType, ex.Message);
    }

    [Fact]
    public async Task InstallFromPackage_RidImplementationForDifferentRuntimeIsRejected()
    {
        string packagePath = BuildRidImplementationNupkg(
            "example.workload.linux-x64",
            "1.0.0",
            "linux-x64");

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        InvalidWorkloadException ex = await Assert.ThrowsAsync<InvalidWorkloadException>(
            () => installer.InstallFromPackageAsync(packagePath));

        Assert.Contains("current RID='win-x64'", ex.Message);
        Assert.Contains("linux-x64", ex.Message);
    }

    private static WorkloadEntry ExistingEntry(string id, string version) => new()
    {
        PackageId = id,
        PackageVersion = version,
        Aliases = [],
        Kind = WorkloadKind.Workload,
        EntryPoint = new EntryPointSpec { AssemblyPath = "Test.dll", Type = "Test.Type" },
        InstallRefCount = 1,
    };

    private static ResolvedPackage NewResolved(string id, string version) => new(
        id,
        NuGetVersion.Parse(version),
        new PackageSource("https://example/v3/index.json", "test"));

    private WorkloadInstaller NewInstaller(
        bool includePrerelease = false,
        IWorkloadMetadataReader? metadataReader = null)
        => new(
            _paths,
            _store,
            metadataReader ?? _metadataReader,
            _catalog,
            _runtimeIdentifierProvider,
            Options.Create(new WorkloadCatalogOptions { IncludePrerelease = includePrerelease }));

    [Fact]
    public async Task InstallFromPackage_HostPackage_SetsExecutableBitOnHostBinary_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string nupkg = BuildNupkg(
            id: "Azure.Functions.Cli.Workloads.Host.osx-arm64",
            payloadFileName: "Azure.Functions.Cli.Workloads.Host");

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        string hostBinary = Path.Combine(
            _paths.GetInstallDirectory(result.Entry.PackageId, result.Entry.PackageVersion),
            "tools", "any", "Azure.Functions.Cli.Workloads.Host");

        File.Exists(hostBinary).Should().BeTrue();
        UnixFileMode mode = File.GetUnixFileMode(hostBinary);
        mode.HasFlag(UnixFileMode.UserExecute).Should().BeTrue();
        mode.HasFlag(UnixFileMode.GroupExecute).Should().BeTrue();
        mode.HasFlag(UnixFileMode.OtherExecute).Should().BeTrue();
    }

    [Fact]
    public async Task InstallFromPackage_NonHostPackage_DoesNotChmodPayload_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // A non-host package that happens to ship a file at the same
        // relative path must not be touched: only Host.* packages opt in.
        string nupkg = BuildNupkg(
            id: "Some.Other.Workload",
            payloadFileName: "Azure.Functions.Cli.Workloads.Host");

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        string payload = Path.Combine(
            _paths.GetInstallDirectory(result.Entry.PackageId, result.Entry.PackageVersion),
            "tools", "any", "Azure.Functions.Cli.Workloads.Host");

        File.Exists(payload).Should().BeTrue();
        UnixFileMode mode = File.GetUnixFileMode(payload);
        mode.HasFlag(UnixFileMode.UserExecute).Should().BeFalse();
    }

    private string BuildNupkg(
        string? tags = null,
        bool includeFuncCliWorkloadType = true,
        string version = "1.0.0",
        string? title = null,
        string description = "For tests.",
        string id = "Test.Workload",
        string payloadFileName = "Test.dll",
        string packageType = WorkloadInstaller.FuncCliWorkloadPackageType,
        bool includePayload = true,
        IEnumerable<(string SourcePath, string TargetPath)>? extraFiles = null)
    {
        string stubAssembly = Path.Combine(_root, $"stub-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(stubAssembly, [0x4D, 0x5A]);

        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = description,
        };
        builder.Authors.Add("test");
        if (title is not null)
        {
            builder.Title = title;
        }
        if (tags is not null)
        {
            foreach (string tag in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                builder.Tags.Add(tag);
            }
        }

        if (includeFuncCliWorkloadType)
        {
            builder.PackageTypes.Add(new PackageType(packageType, new Version(0, 0)));
        }

        if (includePayload)
        {
            builder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = stubAssembly,
                TargetPath = $"tools/{NuGetFramework.Parse("any").GetShortFolderName()}/{payloadFileName}",
            });
        }

        if (extraFiles is not null)
        {
            foreach ((string source, string target) in extraFiles)
            {
                builder.Files.Add(new PhysicalPackageFile { SourcePath = source, TargetPath = target });
            }
        }

        string path = Path.Combine(_root, $"{id}.{Guid.NewGuid():N}.nupkg");
        using (FileStream stream = File.Create(path))
        {
            builder.Save(stream);
        }

        return path;
    }

    private string BuildPointerNupkg(
        string id,
        string version,
        string packagesJson)
    {
        string manifest = $$"""
            {
              "$schema": "{{WorkloadManifestSchema.PackageManifestV1Schema}}",
              "kind": "rid-pointer",
              "displayName": "Example workload",
              "description": "Example pointer.",
              "packages": {{packagesJson}}
            }
            """;
        return BuildNupkg(
            id: id,
            version: version,
            tags: "kind:rid-pointer alias:example",
            includePayload: false,
            extraFiles: [(WriteTempFile("workload.json", manifest), "workload.json")]);
    }

    private string BuildRidImplementationNupkg(
        string id,
        string version,
        string runtimeIdentifier,
        string packageType = WorkloadInstaller.FuncCliWorkloadRidPackageType)
    {
        string manifest = $$"""
            {
              "$schema": "{{WorkloadManifestSchema.PackageManifestV1Schema}}",
              "kind": "content",
              "runtimeIdentifier": "{{runtimeIdentifier}}"
            }
            """;
        return BuildNupkg(
            id: id,
            version: version,
            tags: $"kind:content rid:{runtimeIdentifier}",
            packageType: packageType,
            extraFiles: [(WriteTempFile("workload.json", manifest), "workload.json")]);
    }

    private void InstallImplementationOnDisk(string packageId, string version, string runtimeIdentifier)
    {
        string installDirectory = _paths.GetInstallDirectory(packageId, version);
        Directory.CreateDirectory(installDirectory);
        File.WriteAllText(
            Path.Combine(installDirectory, "workload.json"),
            $$"""
            {
              "$schema": "{{WorkloadManifestSchema.PackageManifestV1Schema}}",
              "kind": "content",
              "runtimeIdentifier": "{{runtimeIdentifier}}"
            }
            """);
    }

    private string WriteTempFile(string name, string contents)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, contents);
        return path;
    }
}
