// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class GlobalManifestStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly WorkloadPaths _paths;
    private readonly GlobalManifestStore _store;

    public GlobalManifestStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _paths = new WorkloadPaths(Options.Create(new WorkloadPathsOptions { Home = _tempHome }));
        _store = new GlobalManifestStore(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }
    }

    [Fact]
    public async Task GetWorkloadsAsync_ReturnsEmpty_WhenManifestMissing()
    {
        var workloads = await _store.GetWorkloadsAsync();

        Assert.Empty(workloads);
    }

    [Fact]
    public async Task SaveWorkloadAsync_InsertsNewEntry()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workload.Dotnet", "1.0.0"));

        var workloads = await _store.GetWorkloadsAsync();
        var entry = Assert.Single(workloads);
        Assert.Equal("Azure.Functions.Cli.Workload.Dotnet", entry.PackageId);
        Assert.Equal("1.0.0", entry.Version);
    }

    [Fact]
    public async Task SaveWorkloadAsync_ReplacesExistingEntry_BySamePackageId()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workload.Dotnet", "1.0.0"));
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workload.Dotnet", "2.0.0"));

        var workloads = await _store.GetWorkloadsAsync();
        var entry = Assert.Single(workloads);
        Assert.Equal("2.0.0", entry.Version);
    }

    [Fact]
    public async Task SaveWorkloadAsync_MatchesPackageId_CaseInsensitively()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workload.Dotnet", "1.0.0"));
        await _store.SaveWorkloadAsync(NewEntry("azure.functions.cli.workload.dotnet", "2.0.0"));

        var workloads = await _store.GetWorkloadsAsync();
        Assert.Single(workloads);
    }

    [Fact]
    public async Task RemoveWorkloadAsync_ReturnsFalse_WhenEntryAbsent()
    {
        var removed = await _store.RemoveWorkloadAsync("does.not.exist");

        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveWorkloadAsync_RemovesEntry_AndReturnsTrue()
    {
        await _store.SaveWorkloadAsync(NewEntry("a", "1.0.0"));
        await _store.SaveWorkloadAsync(NewEntry("b", "1.0.0"));

        var removed = await _store.RemoveWorkloadAsync("A");

        Assert.True(removed);
        var workloads = await _store.GetWorkloadsAsync();
        Assert.Equal(new[] { "b" }, workloads.Select(w => w.PackageId));
    }

    [Fact]
    public async Task GetWorkloadsAsync_RoundTripsAllFields()
    {
        var entry = new GlobalManifestEntry
        {
            PackageId = "Azure.Functions.Cli.Workload.Dotnet",
            DisplayName = "Dotnet",
            Description = "DotNet workload",
            Version = "1.0.0",
            Aliases = new[] { "dotnet", "dotnet-isolated" },
            InstallPath = Path.Combine(_tempHome, "workloads", "dotnet", "1.0.0"),
            EntryPoint = new EntryPointSpec
            {
                Assembly = "lib/net10.0/Foo.dll",
                Type = "Foo.DotnetWorkload",
            },
        };

        await _store.SaveWorkloadAsync(entry);
        var actual = (await _store.GetWorkloadsAsync()).Single();

        Assert.Equal(entry.PackageId, actual.PackageId);
        Assert.Equal(entry.DisplayName, actual.DisplayName);
        Assert.Equal(entry.Description, actual.Description);
        Assert.Equal(entry.Version, actual.Version);
        Assert.Equal(entry.Aliases, actual.Aliases);
        Assert.Equal(entry.InstallPath, actual.InstallPath);
        Assert.Equal(entry.EntryPoint.Assembly, actual.EntryPoint.Assembly);
        Assert.Equal(entry.EntryPoint.Type, actual.EntryPoint.Type);
    }

    [Fact]
    public async Task SaveWorkloadAsync_ThrowsGracefulException_OnMalformedManifest()
    {
        Directory.CreateDirectory(_tempHome);
        File.WriteAllText(_paths.GlobalManifestPath, "{ not valid json");

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => _store.SaveWorkloadAsync(NewEntry("a", "1.0.0")));
        Assert.True(ex.IsUserError);
        Assert.Contains(_paths.GlobalManifestPath, ex.Message);
    }

    [Fact]
    public async Task SaveWorkloadAsync_LeavesNoTempFile_OnSuccess()
    {
        await _store.SaveWorkloadAsync(NewEntry("a", "1.0.0"));

        var stragglers = Directory.GetFiles(_tempHome, "*.json.tmp");
        Assert.Empty(stragglers);
    }

    [Fact]
    public async Task SaveWorkloadAsync_PreservesPreviousManifest_WhenSerializationFails()
    {
        // Establish a baseline good manifest.
        await _store.SaveWorkloadAsync(NewEntry("baseline", "1.0.0"));
        var baselineBytes = File.ReadAllBytes(_paths.GlobalManifestPath);

        // Now use a store that throws mid-serialize and assert the original
        // manifest is byte-identical and no temp file leaks. This is the only
        // test that actually proves the atomic-rename guarantee.
        var failingStore = new ThrowingSerializeStore(_paths);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingStore.SaveWorkloadAsync(NewEntry("would-be-second", "2.0.0")));

        Assert.Equal(baselineBytes, File.ReadAllBytes(_paths.GlobalManifestPath));
        Assert.Empty(Directory.GetFiles(_tempHome, "*.json.tmp"));
    }

    private static GlobalManifestEntry NewEntry(string packageId, string version)
        => new()
        {
            PackageId = packageId,
            DisplayName = packageId,
            Version = version,
            InstallPath = $"/tmp/{packageId}/{version}",
            EntryPoint = new EntryPointSpec { Assembly = "x.dll", Type = "X" },
        };

    private sealed class ThrowingSerializeStore(IWorkloadPaths paths) : GlobalManifestStore(paths)
    {
        internal override Task SerializeAsync(
            Stream stream,
            GlobalManifest manifest,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }
}
