// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class NuGetHostManagerTests : IDisposable
{
    private readonly TestInteractionService _interaction;
    private readonly NuGetHostManager _manager;
    private readonly string _tempDataDir;

    public NuGetHostManagerTests()
    {
        _interaction = new TestInteractionService();
        _manager = new NuGetHostManager(_interaction);

        _tempDataDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDataDir, true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GetInstalledVersions_EmptyHostsDir_ReturnsEmpty()
    {
        var versions = _manager.GetInstalledVersions();
        Assert.NotNull(versions);
    }

    [Fact]
    public void GetInstalledVersions_WithInstalledHost_ReturnsVersion()
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, "99.0.0-test");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll"), "fake");

        try
        {
            var versions = _manager.GetInstalledVersions();
            Assert.Contains(versions, v => v.Version == "99.0.0-test");
        }
        finally
        {
            Directory.Delete(versionDir, true);
        }
    }

    [Fact]
    public void GetInstalledVersions_IncompleteInstall_ExcludesVersion()
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, "99.0.1-test");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "some-other-file.dll"), "fake");

        try
        {
            var versions = _manager.GetInstalledVersions();
            Assert.DoesNotContain(versions, v => v.Version == "99.0.1-test");
        }
        finally
        {
            Directory.Delete(versionDir, true);
        }
    }

    [Fact]
    public void SetDefaultVersion_WritesConfigFile()
    {
        _manager.SetDefaultVersion("99.0.0-test");
        try
        {
            var defaultVersion = _manager.GetDefaultVersion();
            Assert.Equal("99.0.0-test", defaultVersion);
        }
        finally
        {
            var configPath = Path.Combine(HostResolver.GetDataDirectory(), "config.json");
            File.WriteAllText(configPath, "{}\n");
        }
    }

    [Fact]
    public void GetDefaultVersion_NoConfig_ReturnsNull()
    {
        var configPath = Path.Combine(HostResolver.GetDataDirectory(), "config.json");
        var originalContent = File.Exists(configPath) ? File.ReadAllText(configPath) : null;

        try
        {
            File.WriteAllText(configPath, "{}\n");
            var defaultVersion = _manager.GetDefaultVersion();
            Assert.Null(defaultVersion);
        }
        finally
        {
            if (originalContent is not null)
            {
                File.WriteAllText(configPath, originalContent);
            }
        }
    }

    [Fact]
    public void GetDefaultVersion_MalformedJson_ReturnsNull()
    {
        var configPath = Path.Combine(HostResolver.GetDataDirectory(), "config.json");
        var originalContent = File.Exists(configPath) ? File.ReadAllText(configPath) : null;

        try
        {
            File.WriteAllText(configPath, "not valid json{{{");
            var defaultVersion = _manager.GetDefaultVersion();
            Assert.Null(defaultVersion);
        }
        finally
        {
            if (originalContent is not null)
            {
                File.WriteAllText(configPath, originalContent);
            }
            else
            {
                File.WriteAllText(configPath, "{}\n");
            }
        }
    }

    [Fact]
    public void Remove_NonExistentVersion_ReturnsFalse()
    {
        var result = _manager.Remove("nonexistent-version-xyz");
        Assert.False(result);
        Assert.Contains(_interaction.Lines, l => l.Contains("not installed"));
    }

    [Fact]
    public void Remove_ExistingVersion_DeletesDirectory()
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, "99.0.2-test");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll"), "fake");

        var result = _manager.Remove("99.0.2-test");
        Assert.True(result);
        Assert.False(Directory.Exists(versionDir));
    }

    [Fact]
    public void Remove_DefaultVersion_ClearsDefault()
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, "99.0.3-test");
        Directory.CreateDirectory(versionDir);
        File.WriteAllText(Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll"), "fake");
        _manager.SetDefaultVersion("99.0.3-test");

        try
        {
            _manager.Remove("99.0.3-test");
            var defaultVersion = _manager.GetDefaultVersion();
            Assert.Null(defaultVersion);
        }
        finally
        {
            if (Directory.Exists(versionDir))
            {
                Directory.Delete(versionDir, true);
            }
            var configPath = Path.Combine(HostResolver.GetDataDirectory(), "config.json");
            File.WriteAllText(configPath, "{}\n");
        }
    }

    [Fact]
    public async Task InstallAsync_AlreadyInstalled_ReturnsExistingPath()
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, "99.0.4-test");
        Directory.CreateDirectory(versionDir);
        var hostDll = Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
        File.WriteAllText(hostDll, "fake");

        try
        {
            var result = await _manager.InstallAsync("99.0.4-test");
            Assert.Equal(hostDll, result);
            Assert.Contains(_interaction.Lines, l => l.Contains("already installed"));
        }
        finally
        {
            Directory.Delete(versionDir, true);
        }
    }
}
