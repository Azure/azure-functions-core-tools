// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class PublishCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void Publish_IsHidden()
    {
        var command = new PublishCommand(_interaction);

        Assert.True(command.Hidden);
    }

    [Fact]
    public async Task ExecuteAsync_PrintsAzCliSampleAndV4Redirect_AndReturnsNonZero()
    {
        var command = new PublishCommand(_interaction);
        var parseResult = command.Parse([]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not supported in v5", _interaction.AllOutput);
        Assert.Contains("az functionapp deployment", _interaction.AllOutput);
        Assert.Contains("functions-run-local", _interaction.AllOutput);
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresExtraArguments()
    {
        var command = new PublishCommand(_interaction);
        var parseResult = command.Parse(["myapp", "--slot", "staging"]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not supported in v5", _interaction.AllOutput);
    }
}
