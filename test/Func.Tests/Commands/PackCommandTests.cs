// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Tests.Commands;

public class PackCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public void Pack_IsHidden()
    {
        var command = new PackCommand(_interaction);

        command.Hidden.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_PrintsV4Redirect_AndReturnsNonZero()
    {
        var command = new PackCommand(_interaction);
        var parseResult = command.Parse([]);

        var exitCode = await parseResult.InvokeAsync();

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("not supported yet");
        _interaction.AllOutput.Should().Contain("v4");
        _interaction.AllOutput.Should().Contain("functions-run-local");
    }

    [Fact]
    public async Task ExecuteAsync_IgnoresExtraArguments()
    {
        var command = new PackCommand(_interaction);
        var parseResult = command.Parse(["--output", "bin/release", "myapp.zip"]);

        var exitCode = await parseResult.InvokeAsync();

        exitCode.Should().NotBe(0);
        _interaction.AllOutput.Should().Contain("not supported yet");
    }
}
