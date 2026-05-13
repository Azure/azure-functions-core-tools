// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Hosting.AppStacks;
using Azure.Functions.Cli.Hosting.Dashboard;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestInteractionService _interaction = new();
    private readonly FunctionPalette _palette = new();
    private readonly IAppStackProvider _appStackProvider = Substitute.For<IAppStackProvider>();

    public StartCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"func-start-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _appStackProvider.GetStackNameAsync(Arg.Any<WorkingDirectory>(), Arg.Any<CancellationToken>())
            .Returns("unknown");
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
        var cmd = new StartCommand(_interaction, _palette, _appStackProvider);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        Assert.Contains("--port", optionNames);
        Assert.Contains("--cors", optionNames);
        Assert.Contains("--cors-credentials", optionNames);
        Assert.Contains("--functions", optionNames);
        Assert.Contains("--no-build", optionNames);
        Assert.Contains("--enable-auth", optionNames);
        Assert.Contains("--host-version", optionNames);
        Assert.Contains("--output", optionNames);
        Assert.Contains("--no-tui", optionNames);
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

    [Fact]
    public async Task StartCommand_InvalidOutputMode_ThrowsGracefulException()
    {
        var root = TestParser.CreateRoot(_interaction);
        var result = root.Parse($"start \"{_tempDir}\" --output=bogus");

        var config = new InvocationConfiguration { EnableDefaultExceptionHandler = false };
        var ex = await Assert.ThrowsAsync<GracefulException>(() => result.InvokeAsync(config));
        Assert.Contains("--output", ex.Message);
        Assert.Contains("bogus", ex.Message);
    }
}
