// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Hosting.FirstRun;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.FirstRun;

public sealed class FileFirstRunStateStoreTests : IDisposable
{
    private readonly string _tempHome;
    private readonly FileFirstRunStateStore _store;

    public FileFirstRunStateStoreTests()
    {
        _tempHome = Path.Combine(Path.GetTempPath(), $"func-first-run-{Guid.NewGuid():N}");
        _store = new FileFirstRunStateStore(new CliConfigurationPathsOptions(_tempHome));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempHome))
        {
            Directory.Delete(_tempHome, recursive: true);
        }
    }

    [Fact]
    public void IsFirstRun_ReturnsTrue_WhenMarkerMissing()
    {
        Assert.True(_store.IsFirstRun());
    }

    [Fact]
    public async Task MarkCompleteAsync_CreatesMarker_AndIsFirstRunReturnsFalse()
    {
        await _store.MarkCompleteAsync(CancellationToken.None);

        Assert.False(_store.IsFirstRun());
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
        Assert.Throws<ArgumentNullException>(() => new FileFirstRunStateStore(null!));
    }
}
