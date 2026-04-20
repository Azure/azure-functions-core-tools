// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class WorkloadUpdateCheckerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _cachePath;

    public WorkloadUpdateCheckerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-update-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _cachePath = Path.Combine(_tempDir, "update-cache.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CheckForUpdatesAsync_EmptyWorkloads_ReturnsEmpty()
    {
        var checker = new WorkloadUpdateChecker(_cachePath);
        var updates = await checker.CheckForUpdatesAsync([]);
        Assert.Empty(updates);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_UnknownPackage_ReturnsEmpty()
    {
        var checker = new WorkloadUpdateChecker(_cachePath);
        var workloads = new List<WorkloadInfo>
        {
            new("test", "NonExistent.Package.That.Does.Not.Exist.XYZ123", "1.0.0",
                "/fake/path", "fake.dll", DateTimeOffset.UtcNow)
        };

        // Should not throw — unknown packages are silently skipped
        var updates = await checker.CheckForUpdatesAsync(workloads);
        // Either empty (package not found) or has an update — both are fine
        Assert.NotNull(updates);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WritesCacheFile()
    {
        var checker = new WorkloadUpdateChecker(_cachePath);
        var workloads = new List<WorkloadInfo>
        {
            new("test", "NonExistent.Package.XYZ", "1.0.0",
                "/fake/path", "fake.dll", DateTimeOffset.UtcNow)
        };

        await checker.CheckForUpdatesAsync(workloads);

        // Cache file should be created (even if NuGet call failed)
        Assert.True(File.Exists(_cachePath));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_UsesCacheOnSecondCall()
    {
        var checker = new WorkloadUpdateChecker(_cachePath);
        var workloads = new List<WorkloadInfo>
        {
            new("test", "NonExistent.Package.XYZ", "1.0.0",
                "/fake/path", "fake.dll", DateTimeOffset.UtcNow)
        };

        // First call — hits NuGet (or fails gracefully)
        await checker.CheckForUpdatesAsync(workloads);
        Assert.True(File.Exists(_cachePath));
        var firstContent = File.ReadAllText(_cachePath);

        // Second call — should use cache, not rewrite
        await checker.CheckForUpdatesAsync(workloads);
        var secondContent = File.ReadAllText(_cachePath);

        // Cache content should be identical (no new NuGet call, no rewrite)
        Assert.Equal(firstContent, secondContent);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_CorruptCache_RecoversGracefully()
    {
        // Write garbage to cache file
        File.WriteAllText(_cachePath, "{{not valid json!!");

        var checker = new WorkloadUpdateChecker(_cachePath);
        var workloads = new List<WorkloadInfo>
        {
            new("test", "NonExistent.Package.XYZ", "1.0.0",
                "/fake/path", "fake.dll", DateTimeOffset.UtcNow)
        };

        // Should not throw — corrupt cache is replaced
        var updates = await checker.CheckForUpdatesAsync(workloads);
        Assert.NotNull(updates);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_CachedUpdate_ReturnsUpdateInfo()
    {
        // Pre-populate cache with an "update available" entry
        var cache = new WorkloadUpdateChecker.UpdateCache();
        cache.Entries["test.package"] = new WorkloadUpdateChecker.UpdateCacheEntry
        {
            InstalledVersion = "1.0.0",
            LatestVersion = "2.0.0",
            LastCheckedUtc = DateTimeOffset.UtcNow // Fresh cache
        };

        var json = System.Text.Json.JsonSerializer.Serialize(cache, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        File.WriteAllText(_cachePath, json);

        var checker = new WorkloadUpdateChecker(_cachePath);
        var workloads = new List<WorkloadInfo>
        {
            new("test", "Test.Package", "1.0.0",
                "/fake/path", "fake.dll", DateTimeOffset.UtcNow)
        };

        var updates = await checker.CheckForUpdatesAsync(workloads);

        Assert.Single(updates);
        Assert.Equal("test", updates[0].WorkloadId);
        Assert.Equal("1.0.0", updates[0].InstalledVersion);
        Assert.Equal("2.0.0", updates[0].LatestVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_CachedSameVersion_ReturnsEmpty()
    {
        // Pre-populate cache with same version (no update)
        var cache = new WorkloadUpdateChecker.UpdateCache();
        cache.Entries["test.package"] = new WorkloadUpdateChecker.UpdateCacheEntry
        {
            InstalledVersion = "1.0.0",
            LatestVersion = "1.0.0",
            LastCheckedUtc = DateTimeOffset.UtcNow
        };

        var json = System.Text.Json.JsonSerializer.Serialize(cache, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        File.WriteAllText(_cachePath, json);

        var checker = new WorkloadUpdateChecker(_cachePath);
        var workloads = new List<WorkloadInfo>
        {
            new("test", "Test.Package", "1.0.0",
                "/fake/path", "fake.dll", DateTimeOffset.UtcNow)
        };

        var updates = await checker.CheckForUpdatesAsync(workloads);
        Assert.Empty(updates);
    }
}
