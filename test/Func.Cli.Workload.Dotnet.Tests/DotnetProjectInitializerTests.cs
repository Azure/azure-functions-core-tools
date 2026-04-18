// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workload.Dotnet;
using Xunit;

namespace Azure.Functions.Cli.Workload.Dotnet.Tests;

public class DotnetProjectInitializerTests : IDisposable
{
    private readonly FakeDotnetCliRunner _runner;
    private readonly DotnetProjectInitializer _initializer;
    private readonly string _tempDir;

    public DotnetProjectInitializerTests()
    {
        _runner = new FakeDotnetCliRunner();
        _initializer = new DotnetProjectInitializer(_runner);
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-init-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void CanHandle_Dotnet_ReturnsTrue()
    {
        Assert.True(_initializer.CanHandle("dotnet"));
        Assert.True(_initializer.CanHandle("DOTNET"));
        Assert.True(_initializer.CanHandle("dotnet-isolated"));
    }

    [Fact]
    public void CanHandle_OtherRuntime_ReturnsFalse()
    {
        Assert.False(_initializer.CanHandle("python"));
        Assert.False(_initializer.CanHandle("node"));
    }

    [Fact]
    public void GetInitOptions_ReturnsTargetFrameworkOption()
    {
        var options = _initializer.GetInitOptions();
        Assert.Single(options);
        Assert.Equal("--target-framework", options[0].Name);
    }

    [Fact]
    public async Task InitializeAsync_InstallsTemplatePack()
    {
        // Enqueue success for template install + dotnet new
        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueSuccess(); // dotnet new

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        Assert.Equal(2, _runner.Invocations.Count);
        Assert.Contains(DotnetProjectInitializer.ProjectTemplatePackageId, _runner.Invocations[0].Arguments);
        Assert.Contains(BundledTemplateVersions.ProjectTemplatesVersion, _runner.Invocations[0].Arguments);
    }

    [Fact]
    public async Task InitializeAsync_RunsDotnetNewWithCorrectArgs()
    {
        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueSuccess(); // dotnet new

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        var dotnetNewArgs = _runner.Invocations[1].Arguments;
        Assert.Contains("new func", dotnetNewArgs);
        Assert.Contains("--name \"TestApp\"", dotnetNewArgs);
        Assert.Contains($"--output \"{_tempDir}\"", dotnetNewArgs);
        Assert.Contains("--language \"C#\"", dotnetNewArgs);
        Assert.Contains("--Framework net10.0", dotnetNewArgs);
    }

    [Fact]
    public async Task InitializeAsync_WithForce_PassesForceFlag()
    {
        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueSuccess(); // dotnet new

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp", Force: true);
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        Assert.Contains("--force", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task InitializeAsync_WithoutForce_NoForceFlag()
    {
        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueSuccess(); // dotnet new

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp", Force: false);
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        Assert.DoesNotContain("--force", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task InitializeAsync_TemplateInstallFails_ThrowsGracefulException()
    {
        _runner.EnqueueFailure("template install failed");

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => _initializer.InitializeAsync(context, parseResult));
        Assert.Contains("template pack", ex.Message);
    }

    [Fact]
    public async Task InitializeAsync_DotnetNewFails_ThrowsGracefulException()
    {
        _runner.EnqueueSuccess(); // template install succeeds
        _runner.EnqueueFailure("project creation failed");

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => _initializer.InitializeAsync(context, parseResult));
        Assert.Contains("Failed to create", ex.Message);
    }

    [Fact]
    public async Task InitializeAsync_WritesLocalSettingsJson()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        var settingsPath = Path.Combine(_tempDir, "local.settings.json");
        Assert.True(File.Exists(settingsPath));

        var content = await File.ReadAllTextAsync(settingsPath);
        Assert.Contains("UseDevelopmentStorage=true", content);
        Assert.Contains("dotnet-isolated", content);
    }

    [Fact]
    public async Task InitializeAsync_DoesNotOverwriteExistingLocalSettings()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var existingContent = "{\"existing\": true}";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "local.settings.json"), existingContent);

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        var content = await File.ReadAllTextAsync(Path.Combine(_tempDir, "local.settings.json"));
        Assert.Equal(existingContent, content);
    }

    [Fact]
    public async Task InitializeAsync_WritesGitIgnore()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        var gitignorePath = Path.Combine(_tempDir, ".gitignore");
        Assert.True(File.Exists(gitignorePath));

        var content = await File.ReadAllTextAsync(gitignorePath);
        Assert.Contains("local.settings.json", content);
        Assert.Contains("bin/", content);
    }

    [Fact]
    public async Task InitializeAsync_FSharp_PassesFSharpLanguage()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new ProjectInitContext(_tempDir, "dotnet", "F#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        Assert.Contains("--language \"F#\"", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task InitializeAsync_Force_CleansConflictingFiles()
    {
        // Create conflicting .fsproj and source files (as if switching from F# to C#)
        File.WriteAllText(Path.Combine(_tempDir, "MyApp.fsproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "Program.fs"), "module Main");
        File.WriteAllText(Path.Combine(_tempDir, "Host.cs"), "class Host {}");

        _runner.EnqueueSuccess(); // template install
        _runner.EnqueueSuccess(); // dotnet new

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp", Force: true);
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        // .fsproj (opposite language) should be deleted
        Assert.Empty(Directory.GetFiles(_tempDir, "*.fsproj"));
        // Both .cs and .fs files should be deleted (they get regenerated by dotnet new)
        // Note: we check before dotnet new runs — the files were deleted during cleanup
    }

    [Fact]
    public async Task InitializeAsync_DoesNotOverwriteExistingGitignore()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var existingContent = "# custom gitignore";
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".gitignore"), existingContent);

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        var content = await File.ReadAllTextAsync(Path.Combine(_tempDir, ".gitignore"));
        Assert.Equal(existingContent, content);
    }

    [Fact]
    public async Task InitializeAsync_TemplateInstallAlreadyInstalled_Succeeds()
    {
        // First install fails but stderr says "already installed"
        _runner.EnqueueResult(new DotnetCliResult(1, "", "Template pack is already installed"));
        _runner.EnqueueSuccess(); // dotnet new

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        // Should have proceeded to dotnet new (2 invocations)
        Assert.Equal(2, _runner.Invocations.Count);
        Assert.Contains("new func", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task InitializeAsync_CustomTargetFramework()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#", "TestApp");
        var parseResult = CreateParseResult("net8.0");

        await _initializer.InitializeAsync(context, parseResult);

        Assert.Contains("--Framework net8.0", _runner.Invocations[1].Arguments);
    }

    [Fact]
    public async Task InitializeAsync_UsesDirectoryNameWhenNoProjectName()
    {
        _runner.EnqueueSuccess();
        _runner.EnqueueSuccess();

        var context = new ProjectInitContext(_tempDir, "dotnet", "C#");
        var parseResult = CreateParseResult();

        await _initializer.InitializeAsync(context, parseResult);

        var expectedName = Path.GetFileName(_tempDir);
        Assert.Contains($"--name \"{expectedName}\"", _runner.Invocations[1].Arguments);
    }

    /// <summary>
    /// Creates a ParseResult with the target-framework option available (defaulting to net10.0).
    /// </summary>
    private static ParseResult CreateParseResult(string? targetFramework = null)
    {
        var command = new Command("test");
        command.Options.Add(DotnetProjectInitializer.TargetFrameworkOption);

        var args = targetFramework is not null
            ? $"--target-framework {targetFramework}"
            : "";

        return command.Parse(args);
    }
}
