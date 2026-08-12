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
            includePrerelease: false, exact: true, force: false);

        result.Entry.PackageVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_FallsBackToBroadSearchWhenTargetedReturnsZero()
    {
        string nupkg = BuildNupkg(id: "real.workload.id");
        var resolved = NewResolved("real.workload.id", "1.0.0");
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == "node-worker"),
                Arg.Any<CancellationToken>())
            .Returns([]);
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
            includePrerelease: true, exact: false, force: false);

        result.Entry.PackageId.Should().Be("real.workload.id");
        await _catalog.Received(1).SearchAsync(
            Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_AliasResolution_SkipsFallbackWhenTargetedHasHits()
    {
        string nupkg = BuildNupkg(id: "node.pkg");
        var resolved = NewResolved("node.pkg", "1.0.0");
        var source = new PackageSource("https://example/v3/index.json", "test");

        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == "node"),
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new CatalogSearchResult(
                    "node.pkg", NuGetVersion.Parse("1.0.0"), null, null, ["node"], source),
            ]);
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
            Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InstallFromCatalog_LegacyRidAliasesRemainAmbiguous()
    {
        var source = new PackageSource("https://example/v3/index.json", "test");
        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == "python-worker"),
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
                Arg.Is<CatalogSearchQuery>(query => query.Filter == "python-worker"),
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
                Arg.Is<CatalogSearchQuery>(query => query.Filter == "shared-alias"),
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
            includePrerelease: false, exact: false, force: false);

        result.Entry.PackageId.Should().Be(implementationId);
        result.Entry.RuntimeIdentifier.Should().Be("win-x64");
        result.Entry.IsExplicitlyInstalled.Should().BeFalse();
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
