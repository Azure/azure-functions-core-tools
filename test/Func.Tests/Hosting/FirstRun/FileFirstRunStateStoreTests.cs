// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads.Storage;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.FirstRun;

public sealed class FileFirstRunStateStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly string _workloadHome;
    private readonly WorkloadPathsOptions _workloadPaths;
    private readonly FileFirstRunStateStore _store;

    public FileFirstRunStateStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"func-first-run-{Guid.NewGuid():N}");
        _workloadHome = Path.Combine(Path.GetTempPath(), $"func-first-run-workloads-{Guid.NewGuid():N}");
        _workloadPaths = new WorkloadPathsOptions(_workloadHome);
        _store = new FileFirstRunStateStore(new CliConfigurationPathsOptions(_tempHome), _workloadPaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }

        if (Directory.Exists(_workloadHome))
        {
            Directory.Delete(_workloadHome, recursive: true);
        }
    }

    [Fact]
    public async Task IsFirstRunAsync_ReturnsTrue_WhenMarkerMissingAndNoWorkloads()
    {
        Assert.True(await _store.IsFirstRunAsync());
    }

    [Fact]
    public async Task IsFirstRunAsync_ReturnsFalse_WhenWorkloadsInstalled()
    {
        WriteEmptyWorkloadRegistry();

        Assert.False(await _store.IsFirstRunAsync());
    }

    [Fact]
    public async Task GetStateAsync_ReturnsNeverPrompted_WhenMarkerMissingAndNoWorkloads()
    {
        Assert.Equal(FirstRunState.NeverPrompted, await _store.GetStateAsync());
    }

    [Fact]
    public async Task GetStateAsync_ReturnsMarkerWithoutWorkloads_WhenMarkerExistsAndNoWorkloads()
    {
        await _store.MarkCompleteAsync(CancellationToken.None);

        Assert.Equal(FirstRunState.MarkerWithoutWorkloads, await _store.GetStateAsync());
    }

    [Fact]
    public async Task GetStateAsync_ReturnsWorkloadsInstalled_WhenWorkloadsPresent_RegardlessOfMarker()
    {
        WriteEmptyWorkloadRegistry();

        Assert.Equal(FirstRunState.WorkloadsInstalled, await _store.GetStateAsync());

        await _store.MarkCompleteAsync(CancellationToken.None);
        Assert.Equal(FirstRunState.WorkloadsInstalled, await _store.GetStateAsync());
    }

    [Fact]
    public async Task MarkCompleteAsync_CreatesMarker_AndIsFirstRunReturnsFalse()
    {
        await _store.MarkCompleteAsync(CancellationToken.None);

        Assert.False(await _store.IsFirstRunAsync());
        Assert.True(File.Exists(Path.Combine(_tempHome, FileFirstRunStateStore.MarkerFileName)));
    }

    [Fact]
    public async Task MarkCompleteAsync_CreatesHomeDirectory_WhenMissing()
    {
        Assert.False(Directory.Exists(_tempHome));

        await _store.MarkCompleteAsync(CancellationToken.None);

        Assert.True(Directory.Exists(_tempHome));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_ForNullPaths()
    {
        Assert.Throws<ArgumentNullException>(() => new FileFirstRunStateStore(null!, _workloadPaths));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_ForNullWorkloadPaths()
    {
        Assert.Throws<ArgumentNullException>(() => new FileFirstRunStateStore(new CliConfigurationPathsOptions(_tempHome), null!));
    }

    private void WriteEmptyWorkloadRegistry()
    {
        Directory.CreateDirectory(_workloadHome);
        File.WriteAllText(_workloadPaths.WorkloadRegistryPath, "{}");
    }
}
