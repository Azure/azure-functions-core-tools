// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting;

public class HostResolverTests
{
    private readonly TestInteractionService _interaction;
    private readonly HostResolver _resolver;

    public HostResolverTests()
    {
        _interaction = new TestInteractionService();
        _resolver = new HostResolver(_interaction);
    }

    [Fact]
    public void Resolve_WithFuncHostPath_ReturnsEnvVarPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var hostDll = Path.Combine(tempDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
        File.WriteAllText(hostDll, "fake-dll");

        try
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", hostDll);

            var result = _resolver.Resolve(tempDir, requestedVersion: null);

            Assert.NotNull(result);
            Assert.Equal(hostDll, result!.HostPath);
            Assert.Contains("FUNC_HOST_PATH", result.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", null);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Resolve_WithFuncHostPath_NotExist_ReturnsNull()
    {
        try
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", "/nonexistent/path.dll");

            var result = _resolver.Resolve(Path.GetTempPath(), requestedVersion: null);

            Assert.Null(result);
            Assert.Contains(_interaction.Lines, l => l.Contains("does not exist"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", null);
        }
    }

    [Fact]
    public void Resolve_FuncHostPathTakesPrecedenceOverProjectConfig()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var hostDll = Path.Combine(tempDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
        File.WriteAllText(hostDll, "fake-dll");

        File.WriteAllText(Path.Combine(tempDir, ".func-config.json"),
            """{"hostVersion": "99.99.99"}""");

        try
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", hostDll);

            var result = _resolver.Resolve(tempDir, requestedVersion: null);

            Assert.NotNull(result);
            Assert.Contains("FUNC_HOST_PATH", result!.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", null);
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Resolve_ExplicitVersionTakesPrecedenceOverEnvVar()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, "4.1049.0");
        Directory.CreateDirectory(versionDir);
        var hostDll = Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
        File.WriteAllText(hostDll, "fake-dll");

        var envHostDll = Path.Combine(tempDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
        File.WriteAllText(envHostDll, "fake-env-dll");

        try
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", envHostDll);

            var result = _resolver.Resolve(tempDir, requestedVersion: "4.1049.0");

            Assert.NotNull(result);
            Assert.Equal(hostDll, result!.HostPath);
            Assert.Contains("--host-version", result.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FUNC_HOST_PATH", null);
            Directory.Delete(tempDir, true);
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, true);
        }
    }

    [Fact]
    public void Resolve_WithProjectConfig_UsesConfiguredVersion()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"functest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var hostsDir = HostResolver.GetHostsDirectory();
        var versionDir = Path.Combine(hostsDir, "4.1050.0");
        Directory.CreateDirectory(versionDir);
        var hostDll = Path.Combine(versionDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
        File.WriteAllText(hostDll, "fake-dll");

        File.WriteAllText(Path.Combine(tempDir, ".func-config.json"),
            """{"hostVersion": "4.1050.0"}""");

        try
        {
            var result = _resolver.Resolve(tempDir, requestedVersion: null);

            Assert.NotNull(result);
            Assert.Equal(hostDll, result!.HostPath);
            Assert.Contains(".func-config.json", result.Source);
        }
        finally
        {
            Directory.Delete(tempDir, true);
            if (Directory.Exists(versionDir))
                Directory.Delete(versionDir, true);
        }
    }

    [Fact]
    public void Resolve_RequestedVersionNotInstalled_ReturnsNull()
    {
        var result = _resolver.Resolve(Path.GetTempPath(), requestedVersion: "99.99.99");

        Assert.Null(result);
        Assert.Contains(_interaction.Lines, l => l.Contains("not installed"));
    }

    [Fact]
    public void GetInstalledVersions_ReturnsVersionsSorted()
    {
        var hostsDir = HostResolver.GetHostsDirectory();
        var v1 = Path.Combine(hostsDir, "4.1000.0");
        var v2 = Path.Combine(hostsDir, "4.2000.0");
        var v3 = Path.Combine(hostsDir, "4.1500.0");

        Directory.CreateDirectory(v1);
        Directory.CreateDirectory(v2);
        Directory.CreateDirectory(v3);

        try
        {
            var versions = _resolver.GetInstalledVersions();

            var testVersions = versions.Where(v =>
                v == "4.1000.0" || v == "4.2000.0" || v == "4.1500.0").ToList();

            Assert.Equal(3, testVersions.Count);
            Assert.Equal("4.2000.0", testVersions[0]);
            Assert.Equal("4.1500.0", testVersions[1]);
            Assert.Equal("4.1000.0", testVersions[2]);
        }
        finally
        {
            if (Directory.Exists(v1)) Directory.Delete(v1, true);
            if (Directory.Exists(v2)) Directory.Delete(v2, true);
            if (Directory.Exists(v3)) Directory.Delete(v3, true);
        }
    }

    [Fact]
    public void GetDataDirectory_ReturnsNonEmptyPath()
    {
        var dataDir = HostResolver.GetDataDirectory();

        Assert.NotNull(dataDir);
        Assert.NotEmpty(dataDir);
    }

    [Fact]
    public void GetWellKnownHostPaths_ReturnsAtLeastOnePath()
    {
        var paths = HostResolver.GetWellKnownHostPaths().ToList();

        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.Contains("Microsoft.Azure.WebJobs.Script.WebHost.dll", p));
    }
}
