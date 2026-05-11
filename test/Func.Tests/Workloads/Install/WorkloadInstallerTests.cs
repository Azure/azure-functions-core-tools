// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadInstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("workload-installer-").FullName;
    private readonly IWorkloadStore _store = Substitute.For<IWorkloadStore>();
    private readonly IWorkloadMetadataReader _metadataReader = Substitute.For<IWorkloadMetadataReader>();
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
        Assert.True(result.Entry.InstalledExplicitly);

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
                e.InstalledExplicitly &&
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

    private WorkloadInstaller NewInstaller() => new(_paths, _store, _metadataReader);

    private string BuildNupkg(string? tags = null, bool includeFuncCliWorkloadType = true)
    {
        string stubAssembly = Path.Combine(_root, $"stub-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(stubAssembly, [0x4D, 0x5A]);

        var builder = new PackageBuilder
        {
            Id = "Test.Workload",
            Version = NuGetVersion.Parse("1.0.0"),
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
