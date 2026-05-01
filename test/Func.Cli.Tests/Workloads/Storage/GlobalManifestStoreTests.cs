// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class GlobalManifestStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly WorkloadPathsOptions _paths;
    private readonly GlobalManifestStore _store;

    public GlobalManifestStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _paths = new WorkloadPathsOptions { Home = _tempHome };
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
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Dotnet", "1.0.0", NewEntry());

        var workloads = await _store.GetWorkloadsAsync();
        var installed = Assert.Single(workloads);
        Assert.Equal("Azure.Functions.Cli.Workload.Dotnet", installed.PackageId);
        Assert.Equal("1.0.0", installed.Version);
    }

    [Fact]
    public async Task SaveWorkloadAsync_KeepsBothVersions_WhenSamePackageIdDifferentVersion()
    {
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Dotnet", "1.0.0", NewEntry());
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Dotnet", "2.0.0", NewEntry());

        var workloads = await _store.GetWorkloadsAsync();
        Assert.Equal(
            new[] { "1.0.0", "2.0.0" },
            workloads.Select(w => w.Version).OrderBy(v => v));
    }

    [Fact]
    public async Task SaveWorkloadAsync_ReplacesExistingEntry_WhenSamePackageIdAndVersion()
    {
        await _store.SaveWorkloadAsync("pkg", "1.0.0", NewEntry(displayName: "first"));
        await _store.SaveWorkloadAsync("pkg", "1.0.0", NewEntry(displayName: "second"));

        var workloads = await _store.GetWorkloadsAsync();
        var installed = Assert.Single(workloads);
        Assert.Equal("second", installed.Entry.DisplayName);
    }

    [Fact]
    public async Task SaveWorkloadAsync_MatchesPackageId_CaseInsensitively()
    {
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Dotnet", "1.0.0", NewEntry());
        await _store.SaveWorkloadAsync("azure.functions.cli.workload.dotnet", "1.0.0", NewEntry(displayName: "lower"));

        var workloads = await _store.GetWorkloadsAsync();
        var installed = Assert.Single(workloads);
        Assert.Equal("lower", installed.Entry.DisplayName);
    }

    [Fact]
    public async Task RemoveWorkloadAsync_ReturnsFalse_WhenEntryAbsent()
    {
        var removed = await _store.RemoveWorkloadAsync("does.not.exist", "1.0.0");

        Assert.False(removed);
    }

    [Fact]
    public async Task RemoveWorkloadAsync_RemovesSingleVersion_AndKeepsSiblings()
    {
        await _store.SaveWorkloadAsync("pkg", "1.0.0", NewEntry());
        await _store.SaveWorkloadAsync("pkg", "2.0.0", NewEntry());

        var removed = await _store.RemoveWorkloadAsync("PKG", "1.0.0");

        Assert.True(removed);
        var workloads = await _store.GetWorkloadsAsync();
        var installed = Assert.Single(workloads);
        Assert.Equal("2.0.0", installed.Version);
    }

    [Fact]
    public async Task GetWorkloadsAsync_RoundTripsAllFields()
    {
        var entry = new GlobalManifestEntry
        {
            DisplayName = "Dotnet",
            Description = "DotNet workload",
            Aliases = new[] { "dotnet", "dotnet-isolated" },
            InstallPath = Path.Combine(_tempHome, "workloads", "dotnet", "1.0.0"),
            EntryPoint = new EntryPointSpec
            {
                Assembly = "lib/net10.0/Foo.dll",
                Type = "Foo.DotnetWorkload",
            },
        };

        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Dotnet", "1.0.0", entry);
        var actual = (await _store.GetWorkloadsAsync()).Single();

        Assert.Equal("Azure.Functions.Cli.Workload.Dotnet", actual.PackageId);
        Assert.Equal("1.0.0", actual.Version);
        Assert.Equal(entry.DisplayName, actual.Entry.DisplayName);
        Assert.Equal(entry.Description, actual.Entry.Description);
        Assert.Equal(entry.Aliases, actual.Entry.Aliases);
        Assert.Equal(entry.InstallPath, actual.Entry.InstallPath);
        Assert.Equal(entry.EntryPoint.Assembly, actual.Entry.EntryPoint.Assembly);
        Assert.Equal(entry.EntryPoint.Type, actual.Entry.EntryPoint.Type);
    }

    [Fact]
    public async Task PersistedJson_UsesNestedDictionaryShape()
    {
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Dotnet", "1.0.0", NewEntry());
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Dotnet", "2.0.0", NewEntry());
        await _store.SaveWorkloadAsync("Azure.Functions.Cli.Workload.Node", "1.0.0", NewEntry());

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_paths.GlobalManifestPath));
        var workloads = doc.RootElement.GetProperty("workloads");

        var dotnet = workloads.GetProperty("Azure.Functions.Cli.Workload.Dotnet");
        Assert.True(dotnet.TryGetProperty("1.0.0", out _));
        Assert.True(dotnet.TryGetProperty("2.0.0", out _));

        var node = workloads.GetProperty("Azure.Functions.Cli.Workload.Node");
        Assert.True(node.TryGetProperty("1.0.0", out _));

        // Per-version entry must not redundantly carry packageId/version.
        var entry = dotnet.GetProperty("1.0.0");
        Assert.False(entry.TryGetProperty("packageId", out _));
        Assert.False(entry.TryGetProperty("version", out _));
    }

    [Fact]
    public async Task SaveWorkloadAsync_ThrowsGracefulException_OnMalformedManifest()
    {
        Directory.CreateDirectory(_tempHome);
        File.WriteAllText(_paths.GlobalManifestPath, "{ not valid json");

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => _store.SaveWorkloadAsync("a", "1.0.0", NewEntry()));
        Assert.True(ex.IsUserError);
        Assert.Contains(_paths.GlobalManifestPath, ex.Message);
    }

    [Fact]
    public async Task SaveWorkloadAsync_LeavesNoTempFile_OnSuccess()
    {
        await _store.SaveWorkloadAsync("a", "1.0.0", NewEntry());

        var stragglers = Directory.GetFiles(_tempHome, "*.json.tmp");
        Assert.Empty(stragglers);
    }

    [Fact]
    public async Task SaveWorkloadAsync_PreservesPreviousManifest_WhenSerializationFails()
    {
        // Establish a baseline good manifest.
        await _store.SaveWorkloadAsync("baseline", "1.0.0", NewEntry());
        var baselineBytes = File.ReadAllBytes(_paths.GlobalManifestPath);

        // Now use a store that throws mid-serialize and assert the original
        // manifest is byte-identical and no temp file leaks. This is the only
        // test that actually proves the atomic-rename guarantee.
        var failingStore = new ThrowingSerializeStore(_paths);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingStore.SaveWorkloadAsync("would-be-second", "2.0.0", NewEntry()));

        Assert.Equal(baselineBytes, File.ReadAllBytes(_paths.GlobalManifestPath));
        Assert.Empty(Directory.GetFiles(_tempHome, "*.json.tmp"));
    }

    private static GlobalManifestEntry NewEntry(string displayName = "x")
        => new()
        {
            DisplayName = displayName,
            InstallPath = "/tmp/x",
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
