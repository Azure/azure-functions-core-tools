// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadInstallerPackageTests : WorkloadInstallerTestBase
{
    private readonly string _root;
    private readonly IWorkloadStore _store;
    private readonly IWorkloadMetadataReader _metadataReader;
    private readonly IWorkloadCatalog _catalog;
    private readonly IWorkloadPaths _paths;

    public WorkloadInstallerPackageTests()
    {
        _root = Root;
        _store = Store;
        _metadataReader = MetadataReader;
        _catalog = Catalog;
        _paths = Paths;
    }

    [Fact]
    public async Task InstallFromPackage_HappyPath_ExtractsAndPersists()
    {
        string nupkg = BuildNupkg(
            tags: $"{WorkloadPackageTags.AliasPrefix}test {WorkloadPackageTags.AliasPrefix}stub other-tag");

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
            Arg.Is<WorkloadEntry>(entry =>
                entry.PackageId == "test.workload"
                && entry.PackageVersion == "1.0.0"
                && entry.EntryPoint!.AssemblyPath == "Test.dll"
                && entry.DisplayName == "test.workload"
                && entry.Description == "For tests."
                && entry.Source == Path.GetFullPath(nupkg)
                && entry.InstallRefCount == 1
                && entry.Aliases.SequenceEqual(new[] { "test", "stub" })),
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
            .Select(path => Path.GetRelativePath(installDir, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal)];

        entries.Should().Equal(["tools", "tools/any", "tools/any/Test.dll", "workload.json"]);
    }

    [Fact]
    public async Task InstallFromPackage_InvalidWorkloadJson_Throws_RollsBack()
    {
        string nupkg = BuildNupkg();
        _metadataReader.Read(Arg.Any<string>())
            .Returns(_ => throw new InvalidWorkloadException("missing workload.json"));

        WorkloadInstaller installer = NewInstaller();
        Func<Task> act = () => installer.InstallFromPackageAsync(nupkg);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage("*missing workload.json*");
        Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")).Should().BeFalse();
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_MissingFuncCliWorkloadPackageType_Throws_NoExtraction()
    {
        string nupkg = BuildNupkg(includeFuncCliWorkloadType: false);
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.InstallFromPackageAsync(nupkg);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage("*FuncCliWorkload*");
        Directory.Exists(_paths.GetInstallDirectory("test.workload", "1.0.0")).Should().BeFalse();
        await _store.DidNotReceive().SaveWorkloadAsync(Arg.Any<WorkloadEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromPackage_MissingFile_Throws()
    {
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.InstallFromPackageAsync(Path.Combine(_root, "missing.nupkg"));

        await act.Should().ThrowExactlyAsync<FileNotFoundException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task InstallFromPackage_ContentOnly_PersistsContentKind()
    {
        string nupkg = BuildNupkg();
        _metadataReader.Read(Arg.Any<string>())
            .Returns(new WorkloadMetadata
            {
                Schema = "https://example/workload.schema.json",
                Kind = WorkloadKind.Content,
            });

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromPackageAsync(nupkg);

        result.Entry.Kind.Should().Be(WorkloadKind.Content);
        result.Entry.EntryPoint.Should().BeNull();
        result.Entry.DisplayName.Should().Be("test.workload");
        result.Entry.Description.Should().Be("For tests.");

        await _store.Received(1).SaveWorkloadAsync(
            Arg.Is<WorkloadEntry>(entry =>
                entry.Kind == WorkloadKind.Content
                && entry.EntryPoint == null
                && entry.DisplayName == "test.workload"
                && entry.Description == "For tests."),
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

        reports.Should().SatisfyRespectively(
            report => report.Phase.Should().Be(WorkloadInstallPhase.Extracting),
            report => report.Phase.Should().Be(WorkloadInstallPhase.Registering));
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
            Arg.Is<WorkloadEntry>(entry =>
                entry.DisplayName == "Functions Host"
                && entry.Description == "Azure Functions host workload."),
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

        result.Entry.PackageId.Should().Be("example.workload.win-x64");
        result.Entry.LogicalPackage!.Source.Should().Be(Path.GetFullPath(pointerPath));
        await _catalog.DidNotReceive().ResolveVersionAsync(
            Arg.Any<string>(),
            Arg.Any<NuGetVersion>(),
            Arg.Any<string?>(),
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

        Func<Task> act = () => installer.InstallFromPackageAsync(pointerPath);

        await act.Should().ThrowExactlyAsync<FileNotFoundException>()
            .WithMessage("*missing.workload.win-x64*No configured feed*");
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
            WorkloadPackageTypes.Workload);
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());

        Func<Task> act = () => installer.InstallFromPackageAsync(packagePath);

        await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()
            .WithMessage($"*{WorkloadPackageTypes.RuntimeIdentifierPackage}*");
    }

    [Fact]
    public async Task InstallFromPackage_RidImplementationForDifferentRuntimeIsRejected()
    {
        string packagePath = BuildRidImplementationNupkg(
            "example.workload.linux-x64",
            "1.0.0",
            "linux-x64");
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());

        Func<Task> act = () => installer.InstallFromPackageAsync(packagePath);

        InvalidWorkloadException exception =
            (await act.Should().ThrowExactlyAsync<InvalidWorkloadException>()).Which;
        exception.Message.Should().Contain("current RID='win-x64'");
        exception.Message.Should().Contain("linux-x64");
    }

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
}
