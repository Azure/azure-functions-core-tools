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
        var installed = workloads.Should().ContainSingle().Subject;
        installed.EntryPoint!.AssemblyPath.Should().Be("second.dll");
    }

    [Fact]
    public async Task SaveWorkloadAsync_MatchesPackageId_CaseInsensitively()
    {
        await _store.SaveWorkloadAsync(NewEntry("Azure.Functions.Cli.Workloads.Dotnet", "1.0.0", entryAssembly: "first.dll"));
        await _store.SaveWorkloadAsync(NewEntry("azure.functions.cli.workloads.dotnet", "1.0.0", entryAssembly: "lower.dll"));

        var workloads = await _store.GetWorkloadsAsync();
        var installed = workloads.Should().ContainSingle().Subject;
        installed.EntryPoint!.AssemblyPath.Should().Be("lower.dll");
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
        var installed = workloads.Should().ContainSingle().Subject;
        installed.PackageVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task GetWorkloadsAsync_RoundTripsAllFields()
    {
        var entry = new WorkloadEntry
        {
            PackageId = "Azure.Functions.Cli.Workloads.Dotnet",
            PackageVersion = "1.0.0",
            Aliases = ["dotnet", "dotnet-isolated"],
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

    private static WorkloadEntry NewEntry(string packageId, string version, string entryAssembly = "x.dll")
        => new()
        {
            PackageId = packageId,
            PackageVersion = version,
            EntryPoint = new EntryPointSpec { AssemblyPath = entryAssembly, Type = "X" },
        };

    private sealed class ThrowingSerializeStore(IWorkloadPaths paths) : WorkloadStore(paths)
    {
        internal override Task SerializeAsync(Stream stream, WorkloadRegistry registry, CancellationToken cancellationToken)
            => throw new InvalidOperationException("boom");
    }
}
