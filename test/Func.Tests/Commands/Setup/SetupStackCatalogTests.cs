// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Setup;
using Azure.Functions.Cli.Workloads.Catalog;
using NSubstitute;
using NuGet.Configuration;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Tests.Commands.Setup;

public class SetupStackCatalogTests
{
    private readonly IWorkloadCatalog _catalog = Substitute.For<IWorkloadCatalog>();

    [Fact]
    public async Task GetStacksAsync_DiscoversStacksFromKindWorkloadTag()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.java", ["java"], kind: "workload"),
                Result("azure.functions.cli.workloads.host", ["host"], kind: "content"),
                Result("azure.functions.cli.workloads.workers.node", ["node-worker"], kind: "content"),
            ]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.StackNames.Should().BeEquivalentTo(["node", "java"]);
        snapshot.StackPackageId("java").Should().Be("azure.functions.cli.workloads.java");
        snapshot.SupportsStack("host").Should().BeFalse();
        snapshot.SupportsStack("node-worker").Should().BeFalse();
    }

    [Fact]
    public async Task GetStacksAsync_DiscoversJavaAndPowerShell_WhichTheBuiltInListOmits()
    {
        // The built-in fallback only knows node/python/go/dotnet, so java and
        // powershell stacks were skipped silently before catalog discovery.
        SetupDependency.BuiltInStackSnapshot.SupportsStack("java").Should().BeFalse();
        SetupDependency.BuiltInStackSnapshot.SupportsStack("powershell").Should().BeFalse();

        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Result("azure.functions.cli.workloads.java", ["java"], kind: "workload"),
                Result("azure.functions.cli.workloads.powershell", ["powershell"], kind: "workload"),
            ]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.SupportsStack("java").Should().BeTrue();
        snapshot.SupportsStack("powershell").Should().BeTrue();
    }

    [Fact]
    public async Task GetStacksAsync_MapsTemplatesAliasBackToStackName()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Result("azure.functions.cli.workloads.node", ["node"], kind: "workload"),
                Result("azure.functions.cli.workloads.templates.node", ["node-templates"], kind: "content"),
            ]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.SupportsTemplates("node").Should().BeTrue();
        snapshot.TemplatesPackageId("node").Should().Be("azure.functions.cli.workloads.templates.node");
    }

    [Fact]
    public async Task GetStacksAsync_CatalogUnreachable_FallsBackToBuiltInList()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogSearchResult>>(_ => throw new HttpRequestException("offline"));
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.StackNames.Should().BeEquivalentTo(SetupDependency.BuiltInStackSnapshot.StackNames);
    }

    [Fact]
    public async Task GetStacksAsync_EmptyCatalogResult_FallsBackToBuiltInList()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.StackNames.Should().BeEquivalentTo(SetupDependency.BuiltInStackSnapshot.StackNames);
    }

    [Fact]
    public async Task GetStacksAsync_Cancellation_Propagates()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogSearchResult>>(_ => throw new OperationCanceledException());
        SetupStackCatalog stackCatalog = new(_catalog);

        await FluentActions
            .Awaiting(() => stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetStacksAsync_CachesPerSourceAndPrereleaseCombination()
    {
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns([Result("azure.functions.cli.workloads.node", ["node"], kind: "workload")]);
        SetupStackCatalog stackCatalog = new(_catalog);

        await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);
        await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        await _catalog.Received(1).SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStacksAsync_UnexpectedException_Propagates()
    {
        // Only transport/protocol failures fall back. A programming defect must
        // stay visible instead of being silently cached as "offline".
        _catalog.SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<CatalogSearchResult>>(_ => throw new NullReferenceException("bug"));
        SetupStackCatalog stackCatalog = new(_catalog);

        await FluentActions
            .Awaiting(() => stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None))
            .Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task GetStacksAsync_PagesUntilShortPage()
    {
        // A full first page means there may be more; discovery must not stop at
        // the default page size and silently truncate the stack list.
        CatalogSearchResult[] fullPage = [.. Enumerable.Range(0, 100)
            .Select(i => Result($"azure.functions.cli.workloads.filler{i}", [$"filler{i}"], kind: "content"))];
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 0), Arg.Any<CancellationToken>())
            .Returns(fullPage);
        _catalog.SearchAsync(Arg.Is<CatalogSearchQuery>(q => q.Skip == 100), Arg.Any<CancellationToken>())
            .Returns([Result("azure.functions.cli.workloads.java", ["java"], kind: "workload")]);
        SetupStackCatalog stackCatalog = new(_catalog);

        SetupStackSnapshot snapshot = await stackCatalog.GetStacksAsync(source: null, includePrerelease: false, CancellationToken.None);

        snapshot.SupportsStack("java").Should().BeTrue();
        await _catalog.Received(2).SearchAsync(Arg.Any<CatalogSearchQuery>(), Arg.Any<CancellationToken>());
    }

    private static CatalogSearchResult Result(string packageId, string[] aliases, string kind)
        => new(
            packageId,
            new NuGetVersion("1.0.0"),
            Title: null,
            Description: null,
            aliases,
            new PackageSource("https://example.test/v3/index.json"))
        {
            Kind = kind,
        };
}
