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

    [Fact]
    public async Task GetStackNameAsync_CSharpProject_ReturnsDotNet()
    {
        File.WriteAllText(Path.Combine(_tempDir, "FunctionApp.csproj"), "<Project />");

        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal(".NET", stack);
    }

    [Fact]
    public async Task GetStackNameAsync_RequirementsFile_ReturnsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "requirements.txt"), "azure-functions");

        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal("Python", stack);
    }

    [Fact]
    public async Task GetStackNameAsync_NoKnownFiles_ReturnsUnknown()
    {
        string stack = await _provider.GetStackNameAsync(WorkingDirectory.FromExplicit(_tempDir), default);

        Assert.Equal("unknown", stack);
    }
}
