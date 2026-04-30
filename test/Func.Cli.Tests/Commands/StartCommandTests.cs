// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();

    public StartCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-{Guid.NewGuid():N}");
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
    public void StartCommand_HasExpectedOptions()
    {
        var cmd = new StartCommand(_interaction);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--port", optionNames);
        Assert.Contains("--cors", optionNames);
        Assert.Contains("--cors-credentials", optionNames);
        Assert.Contains("--functions", optionNames);
        Assert.Contains("--no-build", optionNames);
        Assert.Contains("--enable-auth", optionNames);
        Assert.Contains("--host-version", optionNames);
    }

    [Fact]
    public async Task StartCommand_PrintsNotImplementedWarning()
    {
        var cmd = new StartCommand(_interaction);
        var parseResult = cmd.Parse([_tempDir]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(1, exitCode);
        Assert.Contains(_interaction.Lines, l => l.Contains("not yet implemented"));
    }

    [Fact]
    public void StartCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        Assert.Contains("start", names);
    }

    [Fact]
    public async Task StartCommand_NonExistentPath_ThrowsGracefulException()
    {
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"start \"{nonExistent}\"");

        // Disable the default exception handler so GracefulException propagates,
        // matching production wiring.
        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = await Assert.ThrowsAsync<GracefulException>(() => result.InvokeAsync(config));
        Assert.Contains("does not exist", ex.Message);
        Assert.Contains(nonExistent, ex.Message);
    }
}
