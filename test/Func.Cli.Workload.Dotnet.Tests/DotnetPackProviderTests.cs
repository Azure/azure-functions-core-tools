// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Xunit;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

public class DotnetPackProviderTests
{
    private readonly FakeDotnetCliRunner _dotnetCli;
    private readonly DotnetPackProvider _provider;

    public DotnetPackProviderTests()
    {
        _dotnetCli = new FakeDotnetCliRunner();
        _provider = new DotnetPackProvider(_dotnetCli);
    }

    [Fact]
    public void WorkerRuntime_IsDotnet()
    {
        Assert.Equal("dotnet", _provider.WorkerRuntime);
    }

    [Fact]
    public async Task ValidateAsync_NoCsproj_ThrowsWhenBuildRequired()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var context = new PackContext(ProjectPath: tempDir);

            var ex = await Assert.ThrowsAsync<GracefulException>(
                () => _provider.ValidateAsync(context));
            Assert.Contains(".csproj or .fsproj", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ValidateAsync_NoBuild_SkipsProjectFileCheck()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var context = new PackContext(ProjectPath: tempDir, NoBuild: true);

            // Should not throw
            await _provider.ValidateAsync(context);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ValidateAsync_WithCsproj_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.csproj"), "<Project/>");
        try
        {
            var context = new PackContext(ProjectPath: tempDir);

            // Should not throw
            await _provider.ValidateAsync(context);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_NoBuild_ReturnsProjectPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var context = new PackContext(ProjectPath: tempDir, NoBuild: true);

            var result = await _provider.PrepareAsync(context);

            Assert.Equal(tempDir, result);
            Assert.Empty(_dotnetCli.Invocations); // No dotnet commands run
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_RunsDotnetPublish()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            _dotnetCli.EnqueueSuccess();
            var context = new PackContext(ProjectPath: tempDir);

            var result = await _provider.PrepareAsync(context);

            Assert.Single(_dotnetCli.Invocations);
            var (args, workDir) = _dotnetCli.Invocations[0];
            Assert.Contains("publish", args);
            Assert.Contains("--configuration Release", args);
            Assert.Equal(tempDir, workDir);
            Assert.EndsWith("publish_output", result);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_PublishFails_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            _dotnetCli.EnqueueFailure("Build failed: missing reference");
            var context = new PackContext(ProjectPath: tempDir);

            var ex = await Assert.ThrowsAsync<GracefulException>(
                () => _provider.PrepareAsync(context));
            Assert.Contains("Failed to build", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CleanupAsync_RemovesPublishOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var publishOutput = Path.Combine(tempDir, "publish_output");
        Directory.CreateDirectory(publishOutput);
        File.WriteAllText(Path.Combine(publishOutput, "test.dll"), "test");
        try
        {
            var context = new PackContext(ProjectPath: tempDir);

            await _provider.CleanupAsync(context, publishOutput);

            Assert.False(Directory.Exists(publishOutput));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task CleanupAsync_NoBuild_DoesNotDeleteProjectPath()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.dll"), "test");
        try
        {
            var context = new PackContext(ProjectPath: tempDir, NoBuild: true);

            await _provider.CleanupAsync(context, tempDir);

            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ValidateAsync_WithFsproj_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "test.fsproj"), "<Project/>");
        try
        {
            var context = new PackContext(ProjectPath: tempDir);
            await _provider.ValidateAsync(context);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_CleansPreExistingPublishOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var publishOutput = Path.Combine(tempDir, "publish_output");
        Directory.CreateDirectory(publishOutput);
        File.WriteAllText(Path.Combine(publishOutput, "old.dll"), "stale");
        try
        {
            _dotnetCli.EnqueueSuccess();
            var context = new PackContext(ProjectPath: tempDir);

            await _provider.PrepareAsync(context);

            // The old file should have been cleaned before publish
            Assert.Single(_dotnetCli.Invocations);
            Assert.Contains("publish", _dotnetCli.Invocations[0].Arguments);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_PublishFails_ShowsStdoutWhenStderrEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            _dotnetCli.EnqueueResult(new DotnetCliResult(1, "restore failed: missing package", ""));
            var context = new PackContext(ProjectPath: tempDir);

            var ex = await Assert.ThrowsAsync<GracefulException>(
                () => _provider.PrepareAsync(context));
            Assert.Contains("Failed to build", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CleanupAsync_NonPublishOutputPath_DoesNotDelete()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var context = new PackContext(ProjectPath: tempDir);

            // packingRoot does NOT end in "publish_output" — should not delete
            await _provider.CleanupAsync(context, tempDir);

            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
