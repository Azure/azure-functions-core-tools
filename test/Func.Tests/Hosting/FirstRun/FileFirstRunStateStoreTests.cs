// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads.Storage;

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
        (await _store.IsFirstRunAsync()).Should().BeTrue();
    }

    [Fact]
    public async Task IsFirstRunAsync_ReturnsFalse_WhenWorkloadsInstalled()
    {
        WriteEmptyWorkloadRegistry();

        (await _store.IsFirstRunAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task GetStateAsync_ReturnsNeverPrompted_WhenMarkerMissingAndNoWorkloads()
    {
        (await _store.GetStateAsync()).Should().Be(FirstRunState.NeverPrompted);
    }

    [Fact]
    public async Task GetStateAsync_ReturnsMarkerWithoutWorkloads_WhenMarkerExistsAndNoWorkloads()
    {
        await _store.MarkCompleteAsync(CancellationToken.None);

        (await _store.GetStateAsync()).Should().Be(FirstRunState.MarkerWithoutWorkloads);
    }

    [Fact]
    public async Task GetStateAsync_ReturnsWorkloadsInstalled_WhenWorkloadsPresent_RegardlessOfMarker()
    {
        WriteEmptyWorkloadRegistry();

        (await _store.GetStateAsync()).Should().Be(FirstRunState.WorkloadsInstalled);

        await _store.MarkCompleteAsync(CancellationToken.None);
        (await _store.GetStateAsync()).Should().Be(FirstRunState.WorkloadsInstalled);
    }

    [Fact]
    public async Task MarkCompleteAsync_CreatesMarker_AndIsFirstRunReturnsFalse()
    {
        await _store.MarkCompleteAsync(CancellationToken.None);

        (await _store.IsFirstRunAsync()).Should().BeFalse();
        File.Exists(Path.Combine(_tempHome, FileFirstRunStateStore.MarkerFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task MarkCompleteAsync_CreatesHomeDirectory_WhenMissing()
    {
        Directory.Exists(_tempHome).Should().BeFalse();

        await _store.MarkCompleteAsync(CancellationToken.None);

        Directory.Exists(_tempHome).Should().BeTrue();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_ForNullPaths()
    {
        FluentActions.Invoking(() => new FileFirstRunStateStore(null!, _workloadPaths)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_ForNullWorkloadPaths()
    {
        FluentActions.Invoking(() => new FileFirstRunStateStore(new CliConfigurationPathsOptions(_tempHome), null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    private void WriteEmptyWorkloadRegistry()
    {
        Directory.CreateDirectory(_workloadHome);
        File.WriteAllText(_workloadPaths.WorkloadRegistryPath, "{}");
    }
}
