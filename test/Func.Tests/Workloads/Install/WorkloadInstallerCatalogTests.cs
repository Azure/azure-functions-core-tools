// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadInstallerCatalogTests : WorkloadInstallerTestBase
{
    private readonly IWorkloadStore _store;
    private readonly IWorkloadMetadataReader _metadataReader;
    private readonly IWorkloadCatalog _catalog;
    private readonly IWorkloadPaths _paths;

    public WorkloadInstallerCatalogTests()
    {
        _store = Store;
        _metadataReader = MetadataReader;
        _catalog = Catalog;
        _paths = Paths;
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
            includePrerelease: false, exact: true, force: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.AlreadyInstalled.Should().BeFalse();
        result.Entry.PackageId.Should().Be("Test.Workload");
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

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            "test.workload", version: null, source: null,
            includePrerelease: false, exact: true, force: false);

        await act.Should().ThrowExactlyAsync<WorkloadPackageNotFoundException>()
            .WithMessage("*test.workload*--prerelease*");
        await _catalog.DidNotReceive().DownloadAsync(Arg.Any<ResolvedPackage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_NoCandidateWithPrereleaseOverride_UsesPrereleaseAndOmitsHint()
    {
        _catalog.ResolveLatestVersionAsync(
                "test.workload", true, null, true, null, Arg.Any<CancellationToken>())
            .Returns((ResolvedPackage?)null);
        WorkloadInstaller installer = NewInstaller(includePrerelease: true);

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            "test.workload", version: null, source: null,
            includePrerelease: null, exact: true, force: false);

        WorkloadPackageNotFoundException exception = (await act.Should()
            .ThrowExactlyAsync<WorkloadPackageNotFoundException>()).Which;
        exception.Message.Should().Contain("test.workload");
        exception.Message.Should().NotContain("--prerelease");
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
        _catalog.ResolveVersionAsync(
                "test.workload", requested, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "test.workload", requested, source: null,
            includePrerelease: false, exact: true, force: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Entry.PackageVersion.Should().Be("1.0.0");
    }

    [Theory]
    [InlineData("comet")]
    [InlineData(null)]
    public async Task InstallFromCatalog_WrongStackInManifest_LeavesCurrentVersionActive(string? declaredStack)
    {
        const string id = "Contoso.DevKit";
        var token = TestContext.Current.CancellationToken;
        var store = new WorkloadStore(_paths);
        var installer = NewInstaller(metadataReader: new WorkloadMetadataReader(), store: store);
        await installer.InstallFromPackageAsync(BuildStackPackage(id, "1.0.0", "node"), cancellationToken: token);
        byte[] registry = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, token);
        string activeManifest = Path.Combine(_paths.GetInstallDirectory(id, "1.0.0"), "workload.json");
        byte[] manifest = await File.ReadAllBytesAsync(activeManifest, token);
        var next = ServePackage(id, BuildStackPackage(id, "2.0.0", declaredStack));

        Func<Task> install = () => installer.InstallFromCatalogAsync(id, next.Version, null, false, true, false, requiredStack: new("node", "win-x64"), cancellationToken: token);

        await install.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*does not provide stack 'node'*");
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, token)).Should().Equal(registry);
        (await File.ReadAllBytesAsync(activeManifest, token)).Should().Equal(manifest);
        Directory.Exists(_paths.GetInstallDirectory(id, "2.0.0")).Should().BeFalse();
        WorkloadActivation.SelectLiveRuntimeEntries(await store.GetWorkloadsAsync(token)).Should().ContainSingle().Which.PackageVersion.Should().Be("1.0.0");
        await _catalog.Received(1).DownloadAsync(next, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("comet", "node", false)]
    [InlineData("node", "comet", false)]
    [InlineData("node", "comet", true)]
    public async Task InstallFromCatalog_WrongPointerOrImplementationStack_DoesNotAttachOwnership(string pointerStack, string implementationStack, bool alreadyOnDisk)
    {
        const string id = "Contoso.DevKit";
        const string impl = "Contoso.DevKit.win-x64";
        var token = TestContext.Current.CancellationToken;
        var store = new WorkloadStore(_paths);
        var installer = NewInstaller(metadataReader: new WorkloadMetadataReader(), store: store);
        await installer.InstallFromPackageAsync(BuildStackPackage(id, "1.0.0", "node"), cancellationToken: token);
        string physicalPackage = BuildStackPackage(impl, "2.0.0", implementationStack, rid: "win-x64");
        if (alreadyOnDisk)
        {
            await installer.InstallFromPackageAsync(physicalPackage, cancellationToken: token);
        }
        byte[] registry = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, token);
        var pointer = ServePackage(id, BuildStackPackage(id, "2.0.0", pointerStack, implementation: impl));
        var physical = ServePackage(impl, physicalPackage, pointer.Source.Source);

        Func<Task> install = () => installer.InstallFromCatalogAsync(id, pointer.Version, null, false, true, false, requiredStack: new("node", "win-x64"), cancellationToken: token);

        await install.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*does not provide stack 'node'*");
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, token)).Should().Equal(registry);
        Directory.Exists(_paths.GetInstallDirectory(impl, "2.0.0")).Should().Be(alreadyOnDisk);
        (await store.GetWorkloadsAsync(token)).Should().OnlyContain(e => e.LogicalPackage == null);
        await _catalog.Received(1).DownloadAsync(pointer, Arg.Any<CancellationToken>());
        await _catalog.Received(pointerStack == "node" && !alreadyOnDisk ? 1 : 0).DownloadAsync(physical, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_RequiredStack_RechecksInstalledManifestBeforeExplicitReuse()
    {
        const string id = "Contoso.DevKit";
        var token = TestContext.Current.CancellationToken;
        var installer = NewInstaller(metadataReader: new WorkloadMetadataReader(), store: new WorkloadStore(_paths));
        var target = ServePackage(id, BuildStackPackage(id, "2.0.0", "node"));
        var required = new WorkloadStackRequirement("NODE", "win-x64");
        var installed = await installer.InstallFromCatalogAsync(id, target.Version, null, false, true, false, requiredStack: required, cancellationToken: token);
        installed.Entry.Stack!.Identifier.Should().Be("node");
        string manifest = Path.Combine(_paths.GetInstallDirectory(id, "2.0.0"), "workload.json");
        string original = await File.ReadAllTextAsync(manifest, token);
        await File.WriteAllTextAsync(manifest, original.Replace("\"node\"", "\"comet\"", StringComparison.Ordinal), token);
        byte[] registry = await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, token);

        Func<Task> reuse = () => installer.InstallFromCatalogAsync(id, target.Version, null, false, true, false, requiredStack: required, cancellationToken: token);

        await reuse.Should().ThrowExactlyAsync<InvalidWorkloadException>().WithMessage("*does not provide stack 'NODE'*");
        (await File.ReadAllBytesAsync(_paths.WorkloadRegistryPath, token)).Should().Equal(registry);
        await _catalog.Received(1).DownloadAsync(target, Arg.Any<CancellationToken>());
    }

    private ResolvedPackage ServePackage(string id, string archive, string? source = null)
    {
        var resolved = NewResolved(id, "2.0.0");
        _catalog.ResolveVersionAsync(Arg.Is<string>(candidate => candidate.Equals(id, StringComparison.OrdinalIgnoreCase)), resolved.Version, source, Arg.Any<CancellationToken>()).Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>()).Returns(_ => File.OpenRead(archive));
        return resolved;
    }

    private string BuildStackPackage(string id, string version, string? stack, string? rid = null, string? implementation = null)
    {
        WorkloadMetadata metadata = new()
        {
            Schema = WorkloadManifestSchema.PackageManifestV1Schema,
            Kind = implementation is null ? WorkloadKind.Workload : WorkloadKind.RidPointer,
            EntryPoint = implementation is null ? new() { AssemblyPath = "Test.dll", Type = "Fixture.Stub" } : null,
            Stack = stack is null ? null : new(stack, stack, stack),
            RuntimeIdentifier = rid,
            Packages = implementation is null ? null : new Dictionary<string, string> { ["win-x64"] = implementation },
        };
        string manifest = WriteTempFile("workload.json", JsonSerializer.Serialize(metadata, WorkloadJsonContext.Default.WorkloadMetadata));
        return BuildNupkg(id: id, version: version, tags: rid is null ? "stack:node" : $"stack:node rid:{rid}", includePayload: implementation is null,
            packageType: rid is null ? WorkloadPackageTypes.Workload : WorkloadPackageTypes.RuntimeIdentifierPackage, extraFiles: [(manifest, "workload.json")]);
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_DoesNotRequireAKeywordMatchOnPackageId()
    {
        string nupkg = BuildNupkg(id: "real.workload.id");
        var resolved = NewResolved("real.workload.id", "1.0.0");
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new CatalogSearchResult(
                    "other.workload", NuGetVersion.Parse("1.0.0"), null, null, ["other"], source),
                new CatalogSearchResult(
                    "real.workload.id", NuGetVersion.Parse("1.0.0"), null, null, ["node-worker"], source),
            ]);
        _catalog.ResolveLatestVersionAsync(
                "real.workload.id", true, null, true, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "node-worker", version: null, source: null,
            includePrerelease: true, exact: false, force: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Entry.PackageId.Should().Be("real.workload.id");
        await _catalog.Received(1).SearchAsync(
            Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_DownloadsTheAdvertisedVersion()
    {
        string nupkg = BuildNupkg(id: "node.pkg");
        var resolved = NewResolved("node.pkg", "1.0.0");
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new CatalogSearchResult(
                    "node.pkg", NuGetVersion.Parse("1.0.0"), null, null, ["node"], source),
            ]);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(nupkg));

        WorkloadInstaller installer = NewInstaller();
        _ = await installer.InstallFromCatalogAsync(
            "node", version: null, source: null,
            includePrerelease: false, exact: false, force: false,
            cancellationToken: TestContext.Current.CancellationToken);

        await _catalog.DidNotReceive().ResolveLatestVersionAsync(
            Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<NuGetVersion?>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_LegacyRidAliasesRemainAmbiguous()
    {
        var source = new PackageSource("https://example/v3/index.json", "test");
        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new CatalogSearchResult(
                    "azure.functions.cli.workloads.workers.python.win-x64",
                    NuGetVersion.Parse("1.0.0"), null, null, ["python-worker"], source)
                {
                    Rid = "win-x64",
                },
                new CatalogSearchResult(
                    "azure.functions.cli.workloads.workers.python.linux-x64",
                    NuGetVersion.Parse("1.0.0"), null, null, ["python-worker"], source)
                {
                    Rid = "linux-x64",
                },
            ]);
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            "python-worker", version: null, source: null,
            includePrerelease: true, exact: false, force: false);

        await act.Should().ThrowExactlyAsync<AmbiguousPackageMatchException>();
    }

    [Fact]
    public async Task InstallFromCatalog_LegacyRidAliasesDoNotUseCurrentRidFallback()
    {
        const string ridA = "fake-rid-a";
        const string ridB = "fake-rid-b";
        var source = new PackageSource("https://example/v3/index.json", "test");
        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new CatalogSearchResult(
                    $"azure.functions.cli.workloads.workers.python.{ridA}",
                    NuGetVersion.Parse("1.0.0"), null, null, ["python-worker"], source)
                {
                    Rid = ridA,
                },
                new CatalogSearchResult(
                    $"azure.functions.cli.workloads.workers.python.{ridB}",
                    NuGetVersion.Parse("1.0.0"), null, null, ["python-worker"], source)
                {
                    Rid = ridB,
                },
            ]);
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            "python-worker", version: null, source: null,
            includePrerelease: true, exact: false, force: false);

        await act.Should().ThrowExactlyAsync<AmbiguousPackageMatchException>()
            .WithMessage("*python-worker*matches multiple packages*");
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_StillAmbiguous_WhenMatchesLackRidTag()
    {
        var source = new PackageSource("https://example/v3/index.json", "test");
        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new CatalogSearchResult(
                    "pkg.one", NuGetVersion.Parse("1.0.0"), null, null, ["shared-alias"], source),
                new CatalogSearchResult(
                    "pkg.two", NuGetVersion.Parse("1.0.0"), null, null, ["shared-alias"], source),
            ]);
        WorkloadInstaller installer = NewInstaller();

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            "shared-alias", version: null, source: null,
            includePrerelease: true, exact: false, force: false);

        await act.Should().ThrowExactlyAsync<AmbiguousPackageMatchException>();
    }

    [Fact]
    public async Task InstallFromCatalog_ExplicitPackageId_BypassesAliasResolution()
    {
        const string explicitId = "Azure.Functions.Cli.Workloads.Workers.Python.win-x64";
        string nupkg = BuildNupkg(
            id: explicitId,
            tags: "rid:win-x64",
            packageType: WorkloadPackageTypes.RuntimeIdentifierPackage);
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
            includePrerelease: true, exact: true, force: false,
            cancellationToken: TestContext.Current.CancellationToken);

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
                    pointerId, NuGetVersion.Parse(version), "Example", "Example pointer", ["example"], source)
                {
                    Kind = "rid-pointer",
                },
                new CatalogSearchResult(
                    implementationId, NuGetVersion.Parse(version), null, null, ["example"], source)
                {
                    Kind = "content",
                    Rid = "win-x64",
                },
            ]);
        _catalog.ResolveLatestVersionAsync(
                pointerId, false, null, true, null, Arg.Any<CancellationToken>())
            .Returns(pointerResolved);
        _catalog.ResolveVersionAsync(
                implementationId, NuGetVersion.Parse(version), source.Source, Arg.Any<CancellationToken>())
            .Returns(implementationResolved);
        _catalog.DownloadAsync(pointerResolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));
        _catalog.DownloadAsync(implementationResolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(implementationPath));

        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());
        WorkloadInstallResult result = await installer.InstallFromCatalogAsync(
            "example", version: null, source: null,
            includePrerelease: false, exact: false, force: false,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Entry.PackageId.Should().Be(implementationId);
        result.Entry.RuntimeIdentifier.Should().Be("win-x64");
        result.Entry.IsImplicitlyInstalled.Should().BeTrue();
        result.Entry.LogicalPackage!.PackageId.Should().Be(pointerId);
        result.Entry.LogicalPackage.PackageVersion.Should().Be(version);
        await _catalog.Received(1).ResolveVersionAsync(
            implementationId, NuGetVersion.Parse(version), source.Source, Arg.Any<CancellationToken>());
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
                "example.workload", false, null, true, null, Arg.Any<CancellationToken>())
            .Returns(resolved);
        _catalog.DownloadAsync(resolved, Arg.Any<CancellationToken>())
            .Returns(_ => File.OpenRead(pointerPath));
        WorkloadInstaller installer = NewInstaller(metadataReader: new WorkloadMetadataReader());

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            "example.workload", version: null, source: null,
            includePrerelease: false, exact: true, force: false);

        await act.Should().ThrowExactlyAsync<WorkloadPackageNotFoundException>()
            .WithMessage("*win-x64*linux-x64, osx-arm64*");
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
                "example.workload", false, null, true, null, Arg.Any<CancellationToken>())
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

        Func<Task> act = () => installer.InstallFromCatalogAsync(
            "example.workload", version: null, source: null,
            includePrerelease: false, exact: true, force: false);

        WorkloadPackageNotFoundException exception =
            (await act.Should().ThrowExactlyAsync<WorkloadPackageNotFoundException>()).Which;
        exception.Message.Should().Contain("partial publish");
        exception.Message.Should().Contain("example.workload.win-x64");
        exception.Message.Should().Contain(resolved.Source.Source);
    }
}
