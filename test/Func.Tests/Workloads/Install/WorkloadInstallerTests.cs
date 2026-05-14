// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Xunit;
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
        _paths = new WorkloadPathsOptions { Home = Path.Combine(_root, ".azure-functions") };
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

        Assert.False(result.AlreadyInstalled);
        Assert.Equal("test.workload", result.Entry.PackageId);
        Assert.Equal("1.0.0", result.Entry.PackageVersion);
        Assert.Equal(["test", "stub"], result.Entry.Aliases);
        Assert.Equal("Test.dll", result.Entry.EntryPoint!.AssemblyPath);
        Assert.Equal(Path.GetFullPath(nupkg), result.Entry.Source);
        Assert.Equal(1, result.Entry.InstallRefCount);

        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Assert.True(Directory.Exists(installDir));
        Assert.True(File.Exists(Path.Combine(installDir, "tools", "any", "Test.dll")));
        Assert.True(File.Exists(nupkg), "Source .nupkg must be left in place.");

        await _store.Received(1).SaveWorkloadAsync(
            Arg.Is<WorkloadEntry>(e =>
                e.PackageId == "test.workload" &&
                e.PackageVersion == "1.0.0" &&
                e.EntryPoint!.AssemblyPath == "Test.dll" &&
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

        Assert.Empty(result.Entry.Aliases);
    }

    [Fact]
    public async Task InstallFromPackage_InvalidWorkloadJson_Throws_RollsBack()
    {
        string nupkg = BuildNupkg();
        _metadataReader.Read(Arg.Any<string>())
            .Returns(_ => throw new InvalidWorkloadException("missing workload.json"));

        WorkloadInstaller installer = NewInstaller();
        InvalidWorkloadException ex = await Assert.ThrowsAsync<InvalidWorkloadException>(
            () => installer.InstallFromPackageAsync(nupkg));

        Assert.Contains("missing workload.json", ex.Message);
        Assert.False(Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")));
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_MissingFuncCliWorkloadPackageType_Throws_NoExtraction()
    {
        string nupkg = BuildNupkg(includeFuncCliWorkloadType: false);

        WorkloadInstaller installer = NewInstaller();
        InvalidWorkloadException ex = await Assert.ThrowsAsync<InvalidWorkloadException>(
            () => installer.InstallFromPackageAsync(nupkg));

        Assert.Contains("FuncCliWorkload", ex.Message);
        Assert.False(Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")));
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_MissingFile_Throws()
    {
        WorkloadInstaller installer = NewInstaller();
        FileNotFoundException ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => installer.InstallFromPackageAsync(Path.Combine(_root, "missing.nupkg")));
        Assert.Contains("does not exist", ex.Message);
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

        Assert.True(result.AlreadyInstalled);
        Assert.Same(priorEntry, result.Entry);
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
        await _store.DidNotReceive().RemoveWorkloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_AlreadyInstalled_DirOnly_NoRegistryEntry_Throws()
    {
        // Half-installed leftover (directory exists but registry has no
        // matching row) is treated as a broken install. The remedy
        // ("pass --force") is the command's responsibility, not the
        // service's, so we only assert on the state description here.
        string installDir = _paths.GetInstallDirectory("test.workload", "1.0.0");
        Directory.CreateDirectory(installDir);

        string nupkg = BuildNupkg();

        WorkloadInstaller installer = NewInstaller();
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.InstallFromPackageAsync(nupkg));
        Assert.Contains("missing from the registry", ex.Message);
        Assert.DoesNotContain("--force", ex.Message);
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

        Assert.False(result.AlreadyInstalled);
        Assert.Equal("test.workload", result.Entry.PackageId);
        Assert.False(File.Exists(stalePath), "Stale files from the prior install must be gone after a forced reinstall.");
        Assert.True(File.Exists(Path.Combine(installDir, "tools", "any", "Test.dll")));
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
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.InstallFromPackageAsync(nupkg));

        Assert.False(Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")));
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

        Assert.True(removed);
        Assert.False(Directory.Exists(installDir));
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

        Assert.False(removed);
        Assert.True(Directory.Exists(installDir));
    }

    [Fact]
    public async Task InstallFromPackage_ContentOnly_PersistsContentKind()
    {
        string nupkg = BuildNupkg();
        _metadataReader.Read(Arg.Any<string>())
            .Returns(new WorkloadMetadata { Schema = "https://example/workload.schema.json", Kind = WorkloadKind.Content });

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        Assert.Equal(WorkloadKind.Content, result.Entry.Kind);
        Assert.Null(result.Entry.EntryPoint);

        await _store.Received(1).SaveWorkloadAsync(
            Arg.Is<WorkloadEntry>(e => e.Kind == WorkloadKind.Content && e.EntryPoint == null),
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

        Assert.False(result.AlreadyInstalled);
        Assert.Equal("test.workload", result.Entry.PackageId);
        Assert.Equal("1.0.0", result.Entry.PackageVersion);
        Assert.True(Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")));
        await _store.Received(1).SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_NoCandidate_Throws()
    {
        _catalog.ResolveLatestVersionAsync(
                "test.workload", false, null, true, null, Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);

        WorkloadInstaller installer = NewInstaller();
        WorkloadPackageNotFoundException ex = await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => installer.InstallFromCatalogAsync(
                "test.workload", version: null, source: null,
                includePrerelease: false, exact: true, force: false));

        Assert.Contains("test.workload", ex.Message);
        Assert.Contains("--prerelease", ex.Message);
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

        Assert.Equal("1.0.0", result.Entry.PackageVersion);
    }

    [Fact]
    public async Task UpdateAsync_NotInstalled_Throws()
    {
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([]);

        WorkloadInstaller installer = NewInstaller();
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.UpdateAsync(
                "test.workload", null, null, false, allowMajor: false));

        Assert.Contains("not installed", ex.Message);
        await _catalog.DidNotReceive().ResolveLatestVersionAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<NuGetVersion?>(),
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

        Assert.True(result.NoUpdateAvailable);
        Assert.True(result.NoCandidateOnSource);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Same(current, result.Entry);
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

        Assert.True(result.NoUpdateAvailable);
        Assert.False(result.NoCandidateOnSource);
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

        Assert.False(result.NoUpdateAvailable);
        Assert.Equal("1.0.0", result.PreviousVersion);
        Assert.Equal("1.1.0", result.Entry.PackageVersion);

        string newDir = _paths.GetInstallDirectory("test.workload", "1.1.0");
        Assert.True(Directory.Exists(newDir));
        Assert.False(Directory.Exists(oldDir), "old install dir must be deleted after swap");

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
        await Assert.ThrowsAsync<IOException>(() => installer.UpdateAsync(
            "test.workload", null, null, false, allowMajor: false));

        Assert.True(Directory.Exists(oldDir), "existing install must remain after staging failure");
        Assert.True(File.Exists(Path.Combine(oldDir, "marker.txt")));
        await _store.DidNotReceive().ReplaceWorkloadAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RequestedVersionNotInstalled_Throws()
    {
        WorkloadEntry current = ExistingEntry("test.workload", "1.0.0");
        _store.GetWorkloadsAsync(Arg.Any<CancellationToken>()).Returns([current]);

        WorkloadInstaller installer = NewInstaller();
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.UpdateAsync(
                "test.workload", NuGetVersion.Parse("0.9.0"), null, false, allowMajor: false));

        Assert.Contains("0.9.0", ex.Message);
        Assert.Contains("not installed", ex.Message);
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

    private WorkloadInstaller NewInstaller() => new(_paths, _store, _metadataReader, _catalog);

    private string BuildNupkg(string? tags = null, bool includeFuncCliWorkloadType = true, string version = "1.0.0")
    {
        string stubAssembly = Path.Combine(_root, $"stub-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(stubAssembly, [0x4D, 0x5A]);

        var builder = new PackageBuilder
        {
            Id = "Test.Workload",
            Version = NuGetVersion.Parse(version),
            Description = "For tests.",
        };
        builder.Authors.Add("test");
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
            TargetPath = $"tools/{NuGetFramework.Parse("any").GetShortFolderName()}/Test.dll",
        });

        string path = Path.Combine(_root, $"Test.Workload.{Guid.NewGuid():N}.nupkg");
        using (FileStream stream = File.Create(path))
        {
            builder.Save(stream);
        }

        return path;
    }
}
