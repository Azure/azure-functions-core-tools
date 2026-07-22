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
        _store.RemoveWorkloadAsync("test.workload", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(true);

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg, force: true);

        result.AlreadyInstalled.Should().BeFalse();
        result.Entry.PackageId.Should().Be("test.workload");
        File.Exists(stalePath).Should().BeFalse("Stale files from the prior install must be gone after a forced reinstall.");
        File.Exists(Path.Combine(installDir, "tools", "any", "Test.dll")).Should().BeTrue();
        await _store.Received(1).RemoveWorkloadAsync("test.workload", "1.0.0", Arg.Any<CancellationToken>());
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
        _store.RemoveWorkloadAsync("test.workload", "1.0.0", Arg.Any<CancellationToken>())
            .Returns(true);

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
        _store.RemoveWorkloadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        WorkloadInstaller installer = NewInstaller();
        bool removed = await installer.UninstallAsync("test.workload", "1.0.0");

        removed.Should().BeFalse();
        Directory.Exists(installDir).Should().BeTrue();
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

        Received.InOrder(() =>
        {
            _store.ReplaceWorkloadAsync(
                "test.workload",
                "1.0.0",
                Arg.Is<WorkloadEntry>(e => e.PackageId == "test.workload" && e.PackageVersion == "1.1.0"),
                Arg.Any<CancellationToken>());
        });
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
        await _store.DidNotReceive().ReplaceWorkloadAsync(
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
        string nupkg = BuildNupkg();
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

        result.Entry.PackageId.Should().Be("test.workload");
        await _catalog.Received(1).SearchAsync(
            Arg.Is<CatalogSearchQuery>(q => q.Filter == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_SkipsFallbackWhenTargetedHasHits()
    {
        // If the targeted search returns any results (even non-matching),
        // we trust the server filter and don't pay for a second broad query.
        string nupkg = BuildNupkg();
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
    public async Task InstallFromCatalog_AliasResolution_DisambiguatesByCurrentRid_WhenAllMatchesAreRidTagged()
    {
        // When an alias spans multiple per-RID packs (host, python worker),
        // the resolver should pick the variant tagged with the current
        // runtime identifier instead of throwing ambiguity.
        string nupkg = BuildNupkg();
        string currentRid = WorkloadRuntimeIdentifier.Current.ToLowerInvariant();
        string targetId = $"Azure.Functions.Cli.Workloads.Workers.Python.{currentRid}";
        var source = new PackageSource("https://example/v3/index.json", "test");
        var resolved = NewResolved(targetId, "1.0.0");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(q => q.Filter == "python-worker"),
                Arg.Any<CancellationToken>())
            .Returns(new List<CatalogSearchResult>
            {
                new("azure.functions.cli.workloads.workers.python.win-x64", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["python-worker"], Source: source) { Rid = "win-x64" },
                new("azure.functions.cli.workloads.workers.python.linux-x64", NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["python-worker"], Source: source) { Rid = "linux-x64" },
                new(targetId.ToLowerInvariant(), NuGetVersion.Parse("1.0.0"),
                    Title: null, Description: null, Aliases: ["python-worker"], Source: source) { Rid = currentRid },
            });
        _catalog.ResolveLatestVersionAsync(
                targetId.ToLowerInvariant(), true, null, true, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "python-worker", version: null, source: null,
            includePrerelease: true, exact: false, force: false);

        result.Should().NotBeNull();
        await _catalog.Received(1).ResolveLatestVersionAsync(
            targetId.ToLowerInvariant(), true, null, true, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_ThrowsClearError_WhenNoPackageForCurrentRid()
    {
        // If the alias only matches per-RID packs and none of them target the
        // current runtime (e.g. python on win-arm64), surface a clear "no
        // package for this RID" message listing what is published instead of
        // a generic "package not found" or alias ambiguity error.
        var source = new PackageSource("https://example/v3/index.json", "test");

        // Use two RIDs that definitely aren't the current host so we exercise
        // the "no pack for current RID" branch on every test environment.
        string currentRid = WorkloadRuntimeIdentifier.Current.ToLowerInvariant();
        string ridA = currentRid == "fake-rid-a" ? "fake-rid-c" : "fake-rid-a";
        string ridB = currentRid == "fake-rid-b" ? "fake-rid-d" : "fake-rid-b";

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
        WorkloadPackageNotFoundException ex = (await FluentActions.Awaiting(() => installer.InstallFromCatalogAsync(
                "python-worker", version: null, source: null,
                includePrerelease: true, exact: false, force: false)).Should().ThrowAsync<WorkloadPackageNotFoundException>()).Which;

        ex.Message.Should().Contain("python-worker");
        ex.Message.Should().Contain(ridA);
        ex.Message.Should().Contain(ridB);
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_StillAmbiguous_WhenMatchesLackRidTag()
    {
        // Two unrelated packages declaring the same alias without any `rid:`
        // tag must still throw ambiguity. RID disambiguation only kicks in
        // when every match is RID-tagged.
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
        string nupkg = BuildNupkg();
        string explicitId = "Azure.Functions.Cli.Workloads.Workers.Python.win-x64";
        var resolved = NewResolved(explicitId, "1.0.0");

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

    private WorkloadInstaller NewInstaller(bool includePrerelease = false)
        => new(_paths, _store, _metadataReader, _catalog, Options.Create(new WorkloadCatalogOptions { IncludePrerelease = includePrerelease }));

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
            builder.PackageTypes.Add(new PackageType(WorkloadInstaller.FuncCliWorkloadPackageType, new Version(0, 0)));
        }

        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = stubAssembly,
            TargetPath = $"tools/{NuGetFramework.Parse("any").GetShortFolderName()}/{payloadFileName}",
        });

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

    private string WriteTempFile(string name, string contents)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}-{name}");
        File.WriteAllText(path, contents);
        return path;
    }
}
