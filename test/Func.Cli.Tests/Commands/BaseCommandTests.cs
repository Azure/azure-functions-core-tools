// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

[Collection(nameof(WorkingDirectoryTests))]
public class BaseCommandTests : IDisposable
{
    // Use a well-known stable path so constructors don't fail when a previous
    // test left cwd in a since-deleted temp directory.
    private static readonly string _safeDir = Path.GetTempPath();
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();

    public BaseCommandTests()
    {
        Directory.SetCurrentDirectory(_safeDir);
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.SetCurrentDirectory(_safeDir); } catch { }
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PathArgument_ChangesWorkingDirectory()
    {
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse($"start {_tempDir}");

        await result.InvokeAsync();

        // Verify cwd changed to the temp dir (use EndsWith to avoid macOS /var vs /private/var)
        var dirName = Path.GetFileName(_tempDir);
        Assert.EndsWith(dirName, Directory.GetCurrentDirectory());
    }

    [Fact]
    public async Task PathArgument_NonExistentDir_ThrowsGracefulException()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse($"start {nonExistent}");

        // Match production behavior: disable default exception handler so GracefulException propagates
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = await Assert.ThrowsAsync<GracefulException>(
            () => result.InvokeAsync(config));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task PathArgument_CreateIfNotExists_CreatesDir()
    {
        var newDir = Path.Combine(_tempDir, "new-project");
        Assert.False(Directory.Exists(newDir));

        // Init uses createIfNotExists: true
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse($"init {newDir}");
        await result.InvokeAsync();

        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void PathArgument_DashPrefix_RejectsAsUnrecognizedOption()
    {
        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse("init --bogus");

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task PathArgument_NoPath_UsesCurrentDirectory()
    {
        Directory.SetCurrentDirectory(_tempDir);

        var root = Parser.CreateCommand(_interaction);
        var result = root.Parse("start");
        await result.InvokeAsync();

        // cwd should remain at _tempDir
        var dirName = Path.GetFileName(_tempDir);
        Assert.EndsWith(dirName, Directory.GetCurrentDirectory());
    }
}
