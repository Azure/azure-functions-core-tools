// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Azure.Functions.Cli.Workloads.Storage;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.FirstRun;

public sealed class FileFirstRunStateStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly IWorkloadStore _workloadStore;
    private readonly FileFirstRunStateStore _store;

    public FileFirstRunStateStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"func-first-run-{Guid.NewGuid():N}");
        _workloadStore = Substitute.For<IWorkloadStore>();
        _workloadStore.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<WorkloadEntry>());
        _store = new FileFirstRunStateStore(new CliConfigurationPathsOptions(_tempHome), _workloadStore);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
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
        _workloadStore.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkloadEntry { PackageId = "Azure.Functions.Cli.Workloads.Node", PackageVersion = "4.0.0" } });

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
        _workloadStore.GetWorkloadsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new WorkloadEntry { PackageId = "Azure.Functions.Cli.Workloads.Node", PackageVersion = "4.0.0" } });

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
        Assert.Throws<ArgumentNullException>(() => new FileFirstRunStateStore(null!, _workloadStore));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_ForNullWorkloadStore()
    {
        Assert.Throws<ArgumentNullException>(() => new FileFirstRunStateStore(new CliConfigurationPathsOptions(_tempHome), null!));
    }
}
