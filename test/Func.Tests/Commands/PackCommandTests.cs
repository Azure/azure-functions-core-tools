// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class PackCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void Pack_IsHidden()
    {
        var command = new PackCommand(_interaction);

        Assert.True(command.Hidden);
    }

    [Fact]
    public async Task ExecuteAsync_PrintsV4Redirect_AndReturnsNonZero()
    {
        var command = new PackCommand(_interaction);
        var parseResult = command.Parse([]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not supported in v5", _interaction.AllOutput);
        Assert.Contains("v4", _interaction.AllOutput);
        Assert.Contains("functions-run-local", _interaction.AllOutput);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresExtraArguments()
    {
        var command = new PackCommand(_interaction);
        var parseResult = command.Parse(["--output", "bin/release", "myapp.zip"]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not supported in v5", _interaction.AllOutput);
    }
}
