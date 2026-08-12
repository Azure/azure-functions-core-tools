// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Workloads.Install;

public sealed class WorkloadPackageSourceTests : IDisposable
{
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();
    private readonly IWorkloadPackageInspector _packageInspector = Substitute.For<IWorkloadPackageInspector>();
    private readonly string _root = Directory.CreateTempSubdirectory("workload-source-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ResolveAsync_ExactPackage_SkipsAliasSearch()
    {
        ResolvedPackage expected = Package("example.workload", "2.0.0");
        _catalog.ResolveLatestVersionAsync(
            expected.PackageId, false, null, true, null, Arg.Any<CancellationToken>()).Returns(expected);
        WorkloadPackageSource source = NewSource();

        ResolvedPackage resolved =
            await source.ResolveAsync(expected.PackageId, version: null, source: null, includePrerelease: null, exact: true);

        resolved.Should().BeSameAs(expected);
        await _catalog.DidNotReceive().SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_AliasPrefersPointerPackage()
    {
        const string alias = "example";
        ResolvedPackage expected = Package("example.pointer", "1.0.0");
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                SearchResult("example.implementation", alias, kind: "rid-package"),
                SearchResult(expected.PackageId, alias, kind: "rid-pointer"),
            ]);
        _catalog.ResolveLatestVersionAsync(
            expected.PackageId, false, null, true, null, Arg.Any<CancellationToken>()).Returns(expected);
        WorkloadPackageSource source = NewSource();

        ResolvedPackage resolved = await source.ResolveAsync(alias, version: null, source: null, includePrerelease: null, exact: false);

        resolved.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ResolveAsync_AmbiguousPointerAlias_Throws()
    {
        const string alias = "example";
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                SearchResult("example.pointer.one", alias, kind: "rid-pointer"),
                SearchResult("example.pointer.two", alias, kind: "rid-pointer"),
            ]);
        WorkloadPackageSource source = NewSource();

        Func<Task> act = () => source.ResolveAsync(alias, version: null, source: null, includePrerelease: null, exact: false);

        await act.Should().ThrowExactlyAsync<AmbiguousPackageMatchException>()
            .WithMessage("*example.pointer.one*example.pointer.two*");
    }

    [Fact]
    public async Task ResolveAsync_EmptyFilteredSearch_RetriesWithoutFilter()
    {
        const string alias = "example";
        ResolvedPackage expected = Package("example.pointer", "1.0.0");
        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == alias),
                Arg.Any<CancellationToken>())
            .Returns([]);
        _catalog.SearchAsync(
                Arg.Is<CatalogSearchQuery>(query => query.Filter == null),
                Arg.Any<CancellationToken>())
            .Returns([SearchResult(expected.PackageId, alias, kind: "rid-pointer")]);
        _catalog.ResolveLatestVersionAsync(
            expected.PackageId, false, null, true, null, Arg.Any<CancellationToken>()).Returns(expected);
        WorkloadPackageSource source = NewSource();

        ResolvedPackage resolved = await source.ResolveAsync(alias, version: null, source: null, includePrerelease: null, exact: false);

        resolved.Should().BeSameAs(expected);
        await _catalog.Received(2).SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_MissingStableVersion_SuggestsPrerelease()
    {
        _catalog.ResolveLatestVersionAsync(
            "example.workload", false, null, true, null, Arg.Any<CancellationToken>()).Returns((ResolvedPackage?)null);
        WorkloadPackageSource source = NewSource();

        Func<Task> act =
            () => source.ResolveAsync("example.workload", version: null, source: null, includePrerelease: false, exact: true);

        await act.Should().ThrowExactlyAsync<WorkloadPackageNotFoundException>()
            .WithMessage("*No matching version*Pass --prerelease*");
    }

    [Fact]
    public async Task ResolveLatestVersionAsync_UsesConfiguredPrereleaseDefault()
    {
        ResolvedPackage expected = Package("example.workload", "2.0.0-preview.1");
        var currentVersion = NuGetVersion.Parse("1.0.0");
        _catalog.ResolveLatestVersionAsync(
            expected.PackageId, true, currentVersion, false, "source", Arg.Any<CancellationToken>()).Returns(expected);
        WorkloadPackageSource source = NewSource(includePrerelease: true);

        ResolvedPackage? resolved =
            await source.ResolveLatestVersionAsync(expected.PackageId, includePrerelease: null, currentVersion, allowMajor: false, source: "source");

        resolved.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ResolveImplementationAsync_UsesExactIdentityAndPointerSource()
    {
        WorkloadPackageSource source = NewSource();
        WorkloadPointerSelection selection = new("win-x64", "example.workload.win-x64");
        InspectedWorkloadPackage pointer = Pointer("example.pointer", "3.0.0");
        ResolvedPackage expected = Package(selection.PackageId, pointer.Identity.Version);
        _catalog.ResolveVersionAsync(
            selection.PackageId, NuGetVersion.Parse(pointer.Identity.Version), "source", Arg.Any<CancellationToken>()).Returns(expected);

        ResolvedPackage resolved = await source.ResolveImplementationAsync(pointer, selection, "source");

        resolved.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task ResolveImplementationAsync_MissingPackage_DescribesPartialPublish()
    {
        WorkloadPackageSource source = NewSource();
        WorkloadPointerSelection selection = new("win-x64", "example.workload.win-x64");
        InspectedWorkloadPackage pointer = Pointer("example.pointer", "3.0.0");

        Func<Task> act = () => source.ResolveImplementationAsync(pointer, selection, "source");

        await act.Should().ThrowExactlyAsync<WorkloadPackageNotFoundException>()
            .WithMessage("*example.pointer*win-x64*example.workload.win-x64*partial publish*");
    }

    [Fact]
    public void FindLocalImplementation_ReturnsSiblingWithExactIdentity()
    {
        string pointerPath = Path.Combine(_root, "example.pointer.1.0.0.nupkg");
        string implementationPath = Path.Combine(_root, "example.workload.win-x64.1.0.0.nupkg");
        File.WriteAllText(pointerPath, string.Empty);
        File.WriteAllText(implementationPath, string.Empty);
        _packageInspector.MatchesIdentity(implementationPath, "example.workload.win-x64", "1.0.0").Returns(true);
        WorkloadPackageSource source = NewSource();

        string result = source.FindLocalImplementation(pointerPath, "example.workload.win-x64", "1.0.0", "win-x64");

        result.Should().Be(implementationPath);
    }

    [Fact]
    public async Task DownloadAsync_CopiesPackageAndDisposalDeletesTemporaryFile()
    {
        ResolvedPackage package = Package("example.workload", "1.0.0");
        _catalog.DownloadAsync(package, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream("package-content"u8.ToArray()));
        var progress = Substitute.For<IProgress<WorkloadInstallProgress>>();
        WorkloadPackageSource source = NewSource();

        TemporaryWorkloadPackageFile download = await source.DownloadAsync(package, progress);
        string path = download.Path;

        File.ReadAllText(path).Should().Be("package-content");
        progress.Received(1).Report(Arg.Is<WorkloadInstallProgress>(value =>
            value.Phase == WorkloadInstallPhase.Downloading
            && value.Description.Contains(package.PackageId, StringComparison.Ordinal)));
        download.Dispose();
        File.Exists(path).Should().BeFalse();
    }

    [Theory]
    [InlineData("file:///packages", true)]
    [InlineData("https://example.test/v3/index.json", false)]
    [InlineData("", false)]
    public void IsLocal_ReturnsExpectedResult(string value, bool expected)
    {
        WorkloadPackageSource source = NewSource();

        bool result = source.IsLocal(value);

        result.Should().Be(expected);
    }

    [Fact]
    public void IsLocal_FullyQualifiedPath_ReturnsTrue()
    {
        WorkloadPackageSource source = NewSource();

        bool result = source.IsLocal(_root);

        result.Should().BeTrue();
    }

    private WorkloadPackageSource NewSource(bool includePrerelease = false)
        => new(_catalog, _packageInspector, Options.Create(new WorkloadCatalogOptions { IncludePrerelease = includePrerelease }));

    private static ResolvedPackage Package(string packageId, string version)
        => new(packageId, NuGetVersion.Parse(version), new PackageSource("https://example.test/v3/index.json"));

    private static CatalogSearchResult SearchResult(string packageId, string alias, string kind)
        => new(
            packageId,
            NuGetVersion.Parse("1.0.0"),
            packageId,
            null,
            [alias],
            new PackageSource("https://example.test/v3/index.json"))
        {
            Kind = kind,
        };

    private static InspectedWorkloadPackage Pointer(string packageId, string version)
        => new(
            "pointer.nupkg",
            new WorkloadPackageIdentity(packageId, version, [], [], [], null, null),
            new WorkloadMetadata
            {
                Schema = WorkloadManifestSchema.PackageManifestV1Schema,
                Kind = WorkloadKind.RidPointer,
            },
            WorkloadPackageRole.Pointer);
}
