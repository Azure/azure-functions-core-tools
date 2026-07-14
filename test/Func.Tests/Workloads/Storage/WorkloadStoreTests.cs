// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Tests.Workloads.Storage;

public class WorkloadStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly WorkloadPathsOptions _paths;
    private readonly WorkloadStore _store;

    public WorkloadStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _paths = new WorkloadPathsOptions(_tempHome);
        _store = new WorkloadStore(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }
    }

    [Fact]
    public async Task GetWorkloadsAsync_ReturnsEmpty_WhenRegistryMissing()
    {
        var workloads = await _store.GetWorkloadsAsync();

        workloads.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveWorkloadAsync_InsertsNewEntry()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Dotnet", "1.0.0"));

        var workloads = await _store.GetWorkloadsAsync();
        var installed = workloads.Should().ContainSingle().Subject;
        installed.PackageId.Should().Be("Azure.Functions.Cli.Workloads.Dotnet");
        installed.PackageVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task SaveWorkloadAsync_KeepsBothVersions_WhenSamePackageIdDifferentVersion()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Dotnet", "1.0.0"));
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Dotnet", "2.0.0"));

        var workloads = await _store.GetWorkloadsAsync();
        workloads.Select(w => w.PackageVersion).OrderBy(v => v).Should().Equal(["1.0.0", "2.0.0"]);
    }

    [Fact]
    public async Task SaveWorkloadAsync_ReplacesExistingEntry_WhenSamePackageIdAndVersion()
    {
        await _store.SaveWorkloadAsync(NewEntry("pkg", "1.0.0", entryAssembly: "first.dll"));
        await _store.SaveWorkloadAsync(NewEntry("pkg", "1.0.0", entryAssembly: "second.dll"));

        var workloads = await _store.GetWorkloadsAsync();
        workloads.Should().ContainSingle().Which.EntryPoint!.AssemblyPath.Should().Be("second.dll");
    }

    [Fact]
    public async Task SaveWorkloadAsync_MatchesPackageId_CaseInsensitively()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Dotnet", "1.0.0", entryAssembly: "first.dll"));
        await _store.SaveWorkloadAsync(NewEntry("azure.functions.cli.workloads.dotnet", "1.0.0", entryAssembly: "lower.dll"));

        var workloads = await _store.GetWorkloadsAsync();
        workloads.Should().ContainSingle().Which.EntryPoint!.AssemblyPath.Should().Be("lower.dll");
    }

    [Fact]
    public async Task RemoveWorkloadAsync_ReturnsFalse_WhenEntryAbsent()
    {
        var removed = await _store.RemoveWorkloadAsync("does.not.exist", "1.0.0");

        removed.Should().BeFalse();
    }

    [Fact]
    public async Task RemoveWorkloadAsync_RemovesSingleVersion_AndKeepsSiblings()
    {
        await _store.SaveWorkloadAsync(NewEntry("pkg", "1.0.0"));
        await _store.SaveWorkloadAsync(NewEntry("pkg", "2.0.0"));

        var removed = await _store.RemoveWorkloadAsync("PKG", "1.0.0");

        removed.Should().BeTrue();
        var workloads = await _store.GetWorkloadsAsync();
        workloads.Should().ContainSingle().Which.PackageVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task GetWorkloadsAsync_RoundTripsAllFields()
    {
        var entry = new WorkloadEntry
        {
            PackageId = "Azure.Functions.Cli.Workloads.Dotnet",
            PackageVersion = "1.0.0",
            Aliases = ["dotnet", "dotnet-isolated"],
            RuntimeIdentifier = "linux-x64",
            IsExplicitlyInstalled = false,
            LogicalPackage = new LogicalPackage
            {
                PackageId = "Azure.Functions.Cli.Workloads.Dotnet",
                PackageVersion = "1.0.0",
                Aliases = ["dotnet"],
                DisplayName = "Azure Functions .NET workload",
                Description = "Logical package.",
                Source = "https://example.test/v3/index.json",
            },
            EntryPoint = new EntryPointSpec
            {
                AssemblyPath = "lib/net10.0/Foo.dll",
                Type = "Foo.DotnetWorkload",
            },
        };

        await _store.SaveWorkloadAsync(entry);
        var actual = (await _store.GetWorkloadsAsync()).Single();

        actual.PackageId.Should().Be(entry.PackageId);
        actual.PackageVersion.Should().Be(entry.PackageVersion);
        actual.Aliases.Should().Equal(entry.Aliases);
        actual.RuntimeIdentifier.Should().Be("linux-x64");
        actual.IsExplicitlyInstalled.Should().BeFalse();
        actual.LogicalPackage!.PackageId.Should().Be(entry.LogicalPackage.PackageId);
        actual.LogicalPackage.Source.Should().Be(entry.LogicalPackage.Source);
        actual.EntryPoint!.AssemblyPath.Should().Be(entry.EntryPoint!.AssemblyPath);
        actual.EntryPoint.Type.Should().Be(entry.EntryPoint.Type);
    }

    [Fact]
    public async Task PersistedJson_UsesFlatArrayShape()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Dotnet", "1.0.0"));
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Dotnet", "2.0.0"));
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Node", "1.0.0"));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(_paths.WorkloadRegistryPath));
        var workloads = doc.RootElement.GetProperty("workloads");

        workloads.ValueKind.Should().Be(JsonValueKind.Array);
        workloads.GetArrayLength().Should().Be(3);

        // Each entry carries its own packageId / packageVersion now that the
        // outer shape is a list rather than a nested dictionary.
        foreach (var element in workloads.EnumerateArray())
        {
            element.TryGetProperty("packageId", out _).Should().BeTrue();
            element.TryGetProperty("packageVersion", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task SaveWorkloadAsync_ThrowsGracefulException_OnMalformedRegistry()
    {
        Directory.CreateDirectory(_tempHome);
        File.WriteAllText(_paths.WorkloadRegistryPath, "{ not valid json");

        var ex = (await FluentActions.Awaiting(() => _store.SaveWorkloadAsync(NewEntry("a", "1.0.0"))).Should().ThrowAsync<GracefulException>()).Which;
        ex.IsUserError.Should().BeTrue();
        ex.Message.Should().Contain(_paths.WorkloadRegistryPath);
    }

    [Fact]
    public async Task SaveWorkloadAsync_LeavesNoTempFile_OnSuccess()
    {
        await _store.SaveWorkloadAsync(NewEntry("a", "1.0.0"));

        var stragglers = Directory.GetFiles(_tempHome, "*.json.tmp");
        stragglers.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveWorkloadAsync_PreservesPreviousRegistry_WhenSerializationFails()
    {
        // Establish a baseline good registry.
        await _store.SaveWorkloadAsync(NewEntry("baseline", "1.0.0"));
        var baselineBytes = File.ReadAllBytes(_paths.WorkloadRegistryPath);

        // Now use a store that throws mid-serialize and assert the original
        // registry is byte-identical and no temp file leaks. This is the only
        // test that actually proves the atomic-rename guarantee.
        var failingStore = new ThrowingSerializeStore(_paths);

        await FluentActions.Awaiting(() => failingStore.SaveWorkloadAsync(NewEntry("would-be-second", "2.0.0"))).Should().ThrowAsync<InvalidOperationException>();

        File.ReadAllBytes(_paths.WorkloadRegistryPath).Should().Equal(baselineBytes);
        Directory.GetFiles(_tempHome, "*.json.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task GetWorkloadsAsync_LegacyEntryDefaultsToExplicitOwnership()
    {
        Directory.CreateDirectory(_tempHome);
        await File.WriteAllTextAsync(
            _paths.WorkloadRegistryPath,
            $$"""
            {
              "$schema": "{{WorkloadManifestSchema.RegistryV1Schema}}",
              "workloads": [
                {
                  "packageId": "legacy.package",
                  "packageVersion": "1.0.0",
                  "installRefCount": 1
                }
              ],
              "metas": []
            }
            """);

        WorkloadEntry entry = Assert.Single(await _store.GetWorkloadsAsync());

        Assert.True(entry.IsExplicitlyInstalled);
        Assert.Null(entry.LogicalPackage);
        Assert.Null(entry.RuntimeIdentifier);
    }

    [Fact]
    public async Task AddOwnershipAsync_AttachesLogicalOwnershipIdempotently()
    {
        await _store.SaveWorkloadAsync(NewEntry("pkg.win-x64", "1.0.0"));
        WorkloadEntry pointerEntry = NewPointerEntry("pkg.win-x64", "1.0.0");

        WorkloadEntry first = await _store.AddOwnershipAsync(pointerEntry, WorkloadOwnershipKind.Logical);
        WorkloadEntry second = await _store.AddOwnershipAsync(pointerEntry, WorkloadOwnershipKind.Logical);

        Assert.True(first.IsExplicitlyInstalled);
        Assert.NotNull(first.LogicalPackage);
        Assert.Equal(2, first.InstallRefCount);
        Assert.Equal(2, second.InstallRefCount);
    }

    [Fact]
    public async Task RemoveOwnershipAsync_RemovesOnlyExplicitOwner()
    {
        WorkloadEntry pointerEntry = NewPointerEntry("pkg.win-x64", "1.0.0");
        WorkloadEntry entry = new()
        {
            PackageId = pointerEntry.PackageId,
            PackageVersion = pointerEntry.PackageVersion,
            Kind = pointerEntry.Kind,
            RuntimeIdentifier = pointerEntry.RuntimeIdentifier,
            LogicalPackage = pointerEntry.LogicalPackage,
            IsExplicitlyInstalled = true,
            InstallRefCount = 2,
        };
        await _store.SaveWorkloadAsync(entry);

        WorkloadOwnershipRemovalResult result = await _store.RemoveOwnershipAsync(
            entry.PackageId,
            entry.PackageVersion,
            WorkloadOwnershipKind.Explicit);

        Assert.True(result.OwnershipRemoved);
        Assert.False(result.EntryRemoved);
        Assert.False(result.Entry!.IsExplicitlyInstalled);
        Assert.NotNull(result.Entry.LogicalPackage);
        Assert.Equal(1, result.Entry.InstallRefCount);
    }

    [Fact]
    public async Task MoveLogicalOwnershipAsync_RetainsExplicitOldRow()
    {
        WorkloadEntry oldEntry = new()
        {
            PackageId = "pkg.win-x64",
            PackageVersion = "1.0.0",
            RuntimeIdentifier = "win-x64",
            IsExplicitlyInstalled = true,
            LogicalPackage = NewPointerEntry("pkg.win-x64", "1.0.0").LogicalPackage,
            InstallRefCount = 2,
        };
        await _store.SaveWorkloadAsync(oldEntry);

        WorkloadOwnershipMoveResult result = await _store.MoveLogicalOwnershipAsync(
            oldEntry.PackageId,
            oldEntry.PackageVersion,
            NewPointerEntry("pkg.win-x64", "2.0.0"));

        Assert.False(result.PreviousEntryRemoved);
        IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync();
        WorkloadEntry retained = Assert.Single(entries, e => e.PackageVersion == "1.0.0");
        Assert.True(retained.IsExplicitlyInstalled);
        Assert.Null(retained.LogicalPackage);
        Assert.Equal(1, retained.InstallRefCount);
        Assert.Single(entries, e => e.PackageVersion == "2.0.0");
    }

    [Fact]
    public void WorkloadKindJsonConverter_RoundTripsRidPointerWireValue()
    {
        string json = JsonSerializer.Serialize(
            new WorkloadMetadata
            {
                Schema = WorkloadManifestSchema.PackageManifestV1Schema,
                Kind = WorkloadKind.RidPointer,
                Packages = new Dictionary<string, string> { ["win-x64"] = "pkg.win-x64" },
            },
            WorkloadJsonContext.Default.WorkloadMetadata);

        Assert.Contains("\"kind\":\"rid-pointer\"", json);
        WorkloadMetadata actual = JsonSerializer.Deserialize(json, WorkloadJsonContext.Default.WorkloadMetadata)!;
        Assert.Equal(WorkloadKind.RidPointer, actual.Kind);
    }

    private static WorkloadEntry NewEntry(string packageId, string version, string entryAssembly = "x.dll")
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            EntryPoint = new EntryPointSpec { AssemblyPath = entryAssembly, Type = "X" },
        };

    private static WorkloadEntry NewPointerEntry(string packageId, string version)
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            Kind = WorkloadKind.Content,
            RuntimeIdentifier = "win-x64",
            IsExplicitlyInstalled = false,
            LogicalPackage = new LogicalPackage
            {
                PackageId = "pkg",
                PackageVersion = version,
                Aliases = ["pkg"],
                DisplayName = "Package",
                Description = "Logical package.",
                Source = "https://example.test/v3/index.json",
            },
            InstallRefCount = 1,
        };

    private sealed class ThrowingSerializeStore(IWorkloadPaths paths) : WorkloadStore(paths)
    {
        internal override Task SerializeAsync(Stream stream, WorkloadRegistry registry, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }
}
