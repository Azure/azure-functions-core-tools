// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.AppStacks;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.AppStacks;

public sealed class DefaultAppStackProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DefaultAppStackProvider _provider = new();

    public DefaultAppStackProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-stack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("FunctionApp.csproj")]
    [InlineData("FunctionApp.fsproj")]
    [InlineData("FunctionApp.vbproj")]
    [InlineData("FunctionApp.sln")]
    [InlineData("FunctionApp.slnx")]
    public async Task GetStackNameAsync_DotNetProject_ReturnsDotNet(string fileName)
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), string.Empty);

        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal(".NET", stack);
    }

    [Theory]
    [InlineData("requirements.txt")]
    [InlineData("pyproject.toml")]
    [InlineData("function_app.py")]
    public async Task GetStackNameAsync_PythonProject_ReturnsPython(string fileName)
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), string.Empty);

        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal("Python", stack);
    }

    [Theory]
    [InlineData("pom.xml")]
    [InlineData("build.gradle")]
    [InlineData("build.gradle.kts")]
    [InlineData("FunctionApp.java")]
    public async Task GetStackNameAsync_JavaProject_ReturnsJava(string fileName)
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), string.Empty);

        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal("Java", stack);
    }

    [Theory]
    [InlineData("profile.ps1")]
    [InlineData("requirements.psd1")]
    [InlineData("run.ps1")]
    public async Task GetStackNameAsync_PowerShellProject_ReturnsPowerShell(string fileName)
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), string.Empty);

        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal("PowerShell", stack);
    }

    [Theory]
    [InlineData("package.json")]
    [InlineData("index.js")]
    [InlineData("index.ts")]
    public async Task GetStackNameAsync_NodeProject_ReturnsNode(string fileName)
    {
        File.WriteAllText(Path.Combine(_tempDir, fileName), string.Empty);

        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal("Node.js", stack);
    }

    [Fact]
    public async Task GetStackNameAsync_NoKnownFiles_ReturnsUnknown()
    {
        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal("unknown", stack);
    }
}
