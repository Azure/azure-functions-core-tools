// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class VersionCommandTests
{
    private readonly TestInteractionService _interaction;

    public VersionCommandTests()
    {
        _interaction = new TestInteractionService();
    }

    [Fact]
    public async Task ExecuteAsync_PrintsVersionString()
    {
        var command = new VersionCommand(_interaction);
        var parseResult = command.Parse([]);

        var exitCode = await parseResult.InvokeAsync();

        Assert.Equal(0, exitCode);
        Assert.Single(_interaction.Lines);
        Assert.Contains("5.0.0", _interaction.Lines[0]);
    }

    [Fact]
    public void GetVersion_ReturnsNonEmptyString()
    {
        var version = VersionCommand.GetVersion();

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Contains("5.0.0", version);
    }

    [Fact]
    public void ExecuteDetailed_PrintsTable()
    {
        var command = new VersionCommand(_interaction);

        var exitCode = command.ExecuteDetailed();

        Assert.Equal(0, exitCode);
        Assert.Contains(_interaction.Lines, l => l.Contains("Azure Functions CLI"));
        Assert.Contains("Version", _interaction.AllOutput);
        Assert.Contains("Runtime", _interaction.AllOutput);
    }
}
