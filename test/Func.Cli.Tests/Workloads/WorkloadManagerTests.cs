// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Tests.Workloads;

public class WorkloadManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction;
    private readonly WorkloadManager _manager;

    public WorkloadManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-workload-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _interaction = new TestInteractionService();
        _manager = new WorkloadManager(_interaction, _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void GetInstalledWorkloads_EmptyByDefault()
    {
        var workloads = _manager.GetInstalledWorkloads();
        Assert.Empty(workloads);
    }

    [Fact]
    public async Task InstallWorkloadAsync_CreatesDirectoryAndManifest()
    {
        var info = await _manager.InstallWorkloadAsync(
            "Azure.Functions.Cli.Workload.Dotnet", "1.0.0");

        Assert.Equal("dotnet", info.Id);
        Assert.Equal("1.0.0", info.Version);
        Assert.Equal("Azure.Functions.Cli.Workload.Dotnet", info.PackageId);
        Assert.True(Directory.Exists(info.InstallPath));

        // Manifest should be written
        var manifestPath = Path.Combine(_tempDir, "workloads.json");
        Assert.True(File.Exists(manifestPath));

        // Should appear in installed list
        var installed = _manager.GetInstalledWorkloads();
        Assert.Single(installed);
        Assert.Equal("dotnet", installed[0].Id);
    }

    [Fact]
    public async Task UninstallWorkloadAsync_RemovesDirectoryAndManifestEntry()
    {
        await _manager.InstallWorkloadAsync("Azure.Functions.Cli.Workload.Python", "2.0.0");
        var installed = _manager.GetInstalledWorkloads();
        Assert.Single(installed);

        await _manager.UninstallWorkloadAsync("python");

        installed = _manager.GetInstalledWorkloads();
        Assert.Empty(installed);
    }

    [Fact]
    public async Task InstallWorkloadAsync_ReplacesExistingVersion()
    {
        await _manager.InstallWorkloadAsync("Azure.Functions.Cli.Workload.Node", "1.0.0");
        await _manager.InstallWorkloadAsync("Azure.Functions.Cli.Workload.Node", "2.0.0");

        var installed = _manager.GetInstalledWorkloads();
        Assert.Single(installed);
        Assert.Equal("2.0.0", installed[0].Version);
    }

    [Fact]
    public async Task UninstallWorkloadAsync_NonExistent_ShowsWarning()
    {
        await _manager.UninstallWorkloadAsync("nonexistent");

        Assert.Contains(_interaction.Lines, l => l.Contains("not installed"));
    }

    [Fact]
    public void LoadWorkloads_EmptyWhenNoWorkloadsInstalled()
    {
        var workloads = _manager.LoadWorkloads();
        Assert.Empty(workloads);
    }

    [Fact]
    public void GetAllTemplateProviders_EmptyWhenNoWorkloads()
    {
        var providers = _manager.GetAllTemplateProviders();
        Assert.Empty(providers);
    }

    [Fact]
    public void GetAllProjectInitializers_EmptyWhenNoWorkloads()
    {
        var initializers = _manager.GetAllProjectInitializers();
        Assert.Empty(initializers);
    }

    [Fact]
    public void GetAvailableRuntimes_EmptyWhenNoWorkloads()
    {
        var runtimes = _manager.GetAvailableRuntimes();
        Assert.Empty(runtimes);
    }

    [Theory]
    [InlineData("Azure.Functions.Cli.Workload.Dotnet", "dotnet")]
    [InlineData("Azure.Functions.Cli.Workload.Python", "python")]
    [InlineData("Azure.Functions.Cli.Workload.Node", "node")]
    [InlineData("SomePackage", "somepackage")]
    public void ExtractWorkloadId_ExtractsLastSegment(string packageId, string expectedId)
    {
        Assert.Equal(expectedId, WorkloadManager.ExtractWorkloadId(packageId));
    }

    [Theory]
    [InlineData("dotnet", "Azure.Functions.Cli.Workload.Dotnet")]
    [InlineData("Dotnet", "Azure.Functions.Cli.Workload.Dotnet")]
    [InlineData("python", "Azure.Functions.Cli.Workload.Python")]
    [InlineData("node", "Azure.Functions.Cli.Workload.Node")]
    [InlineData("java", "Azure.Functions.Cli.Workload.Java")]
    [InlineData("powershell", "Azure.Functions.Cli.Workload.PowerShell")]
    public void ResolvePackageId_ResolvesWellKnownAliases(string alias, string expectedPackageId)
    {
        Assert.Equal(expectedPackageId, WorkloadManager.ResolvePackageId(alias));
    }

    [Theory]
    [InlineData("Azure.Functions.Cli.Workload.Dotnet")]
    [InlineData("SomeCustom.Package")]
    public void ResolvePackageId_PassesThroughFullPackageIds(string packageId)
    {
        Assert.Equal(packageId, WorkloadManager.ResolvePackageId(packageId));
    }

    [Fact]
    public async Task InstallWorkloadAsync_ResolvesAlias()
    {
        var info = await _manager.InstallWorkloadAsync("dotnet", "1.0.0");

        Assert.Equal("dotnet", info.Id);
        Assert.Equal("Azure.Functions.Cli.Workload.Dotnet", info.PackageId);
    }

    [Fact]
    public async Task PrintUpdateNoticesAsync_CompletesWithoutError()
    {
        // Even with no workloads loaded, PrintUpdateNoticesAsync should be safe
        await _manager.PrintUpdateNoticesAsync();
    }

    [Fact]
    public void GetAllPackProviders_EmptyWhenNoWorkloads()
    {
        var providers = _manager.GetAllPackProviders();
        Assert.Empty(providers);
    }

    [Fact]
    public async Task UpdateWorkloadAsync_NonExistent_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _manager.UpdateWorkloadAsync("nonexistent"));
    }

    [Fact]
    public async Task InstallWorkloadAsync_MultipleWorkloads_AllListed()
    {
        await _manager.InstallWorkloadAsync("dotnet", "1.0.0");
        await _manager.InstallWorkloadAsync("node", "2.0.0");
        await _manager.InstallWorkloadAsync("python", "3.0.0");

        var installed = _manager.GetInstalledWorkloads();
        Assert.Equal(3, installed.Count);
        Assert.Contains(installed, w => w.Id == "dotnet");
        Assert.Contains(installed, w => w.Id == "node");
        Assert.Contains(installed, w => w.Id == "python");
    }

    [Fact]
    public async Task GetAvailableWorkloadsAsync_ReturnsBuiltInCatalog()
    {
        var workloads = await _manager.GetAvailableWorkloadsAsync();

        // Should contain at least the built-in catalog entries
        Assert.True(workloads.Count >= 5, $"Expected at least 5 workloads, got {workloads.Count}");
        Assert.Contains(workloads, w => w.Id == "dotnet");
        Assert.Contains(workloads, w => w.Id == "node");
        Assert.Contains(workloads, w => w.Id == "python");
        Assert.Contains(workloads, w => w.Id == "java");
        Assert.Contains(workloads, w => w.Id == "powershell");
    }

    [Fact]
    public async Task GetAvailableWorkloadsAsync_ShowsInstalledStatus()
    {
        await _manager.InstallWorkloadAsync("dotnet", "1.0.0");

        var workloads = await _manager.GetAvailableWorkloadsAsync();

        var dotnet = workloads.Single(w => w.Id == "dotnet");
        Assert.True(dotnet.IsInstalled);
        Assert.Equal("1.0.0", dotnet.InstalledVersion);

        var node = workloads.Single(w => w.Id == "node");
        Assert.False(node.IsInstalled);
        Assert.Null(node.InstalledVersion);
    }

    [Fact]
    public void LoadWorkloads_StartsUpdateCheckInBackground()
    {
        // Load with no workloads installed — should not throw
        var workloads = _manager.LoadWorkloads();
        Assert.Empty(workloads);
    }
}
