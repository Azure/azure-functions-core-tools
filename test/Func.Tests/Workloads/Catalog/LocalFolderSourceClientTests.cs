// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Catalog;

public sealed class LocalFolderSourceClientTests : IDisposable
{
    private const string FuncCliWorkloadPackageType = "FuncCliWorkload";

    private readonly string _root = Directory.CreateTempSubdirectory("workload-local-feed-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task SearchAsync_ReturnsOnlyFuncCliWorkloadPackages()
    {
        BuildNupkg("workload.alpha", "1.0.0", includeFuncCliWorkloadType: true, tags: "alias:alpha");
        BuildNupkg("regular.lib", "1.0.0", includeFuncCliWorkloadType: false);

        LocalFolderSourceClient client = NewClient();

        IReadOnlyList<CatalogSearchResult> results = await client.SearchAsync(query: null, includePrerelease: false, skip: 0, take: 50, default);

        CatalogSearchResult only = Assert.Single(results);
        Assert.Equal("workload.alpha", only.PackageId);
        Assert.Equal(["alpha"], only.Aliases);
    }

    [Fact]
    public async Task SearchAsync_KeepsHighestVersionPerId()
    {
        BuildNupkg("workload.alpha", "1.0.0");
        BuildNupkg("workload.alpha", "2.0.0");

        LocalFolderSourceClient client = NewClient();

        IReadOnlyList<CatalogSearchResult> results = await client.SearchAsync(null, false, 0, 50, default);

        CatalogSearchResult only = Assert.Single(results);
        Assert.Equal(NuGetVersion.Parse("2.0.0"), only.LatestVersion);
    }

    [Fact]
    public async Task SearchAsync_ExcludesPrereleaseByDefault()
    {
        BuildNupkg("workload.alpha", "1.0.0");
        BuildNupkg("workload.alpha", "2.0.0-beta.1");

        LocalFolderSourceClient client = NewClient();

        IReadOnlyList<CatalogSearchResult> stable = await client.SearchAsync(null, includePrerelease: false, 0, 50, default);
        Assert.Equal(NuGetVersion.Parse("1.0.0"), Assert.Single(stable).LatestVersion);

        IReadOnlyList<CatalogSearchResult> withPrerelease = await client.SearchAsync(null, includePrerelease: true, 0, 50, default);
        Assert.Equal(NuGetVersion.Parse("2.0.0-beta.1"), Assert.Single(withPrerelease).LatestVersion);
    }

    [Fact]
    public async Task ListVersionsAsync_ReturnsAllVersionsForId_OrderedAscending()
    {
        BuildNupkg("workload.alpha", "2.0.0");
        BuildNupkg("workload.alpha", "1.0.0");
        BuildNupkg("workload.beta", "1.0.0");

        LocalFolderSourceClient client = NewClient();

        IReadOnlyList<NuGetVersion> versions = await client.ListVersionsAsync("workload.alpha", default);

        Assert.Equal([NuGetVersion.Parse("1.0.0"), NuGetVersion.Parse("2.0.0")], versions);
    }

    [Fact]
    public async Task OpenPackageAsync_ReturnsReadableNupkgStream()
    {
        BuildNupkg("workload.alpha", "1.2.3");
        LocalFolderSourceClient client = NewClient();

        await using Stream stream = await client.OpenPackageAsync("workload.alpha", NuGetVersion.Parse("1.2.3"), default);

        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);

        // The stream is a real .nupkg, so PackageArchiveReader should accept it.
        using var reader = new PackageArchiveReader(stream);
        Assert.Equal("workload.alpha", reader.NuspecReader.GetId());
    }

    [Fact]
    public async Task OpenPackageAsync_MissingVersion_Throws()
    {
        BuildNupkg("workload.alpha", "1.0.0");
        LocalFolderSourceClient client = NewClient();

        await Assert.ThrowsAsync<WorkloadPackageNotFoundException>(
            () => client.OpenPackageAsync("workload.alpha", NuGetVersion.Parse("9.9.9"), default));
    }

    private LocalFolderSourceClient NewClient()
    {
        var source = new PackageSource("local", new Uri(_root, UriKind.Absolute), IsLocal: true);
        return new LocalFolderSourceClient(source);
    }

    private void BuildNupkg(string id, string version, bool includeFuncCliWorkloadType = true, string? tags = null)
    {
        string stubAssembly = Path.Combine(_root, $"stub-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(stubAssembly, [0x4D, 0x5A]);

        var builder = new PackageBuilder
        {
            Id = id,
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
            builder.PackageTypes.Add(new PackageType(FuncCliWorkloadPackageType, new Version(0, 0)));
        }

        builder.Files.Add(new PhysicalPackageFile
        {
            SourcePath = stubAssembly,
            TargetPath = $"tools/{NuGetFramework.Parse("any").GetShortFolderName()}/Test.dll",
        });

        string path = Path.Combine(_root, $"{id}.{version}.nupkg");
        using FileStream stream = File.Create(path);
        builder.Save(stream);
    }
}
