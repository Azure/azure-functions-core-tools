// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

[Collection(nameof(WorkingDirectoryTests))]
public class PackCommandTests
{
    private readonly TestInteractionService _interaction = new();

    [Fact]
    public async Task ExecuteAsync_NoWorkloadInstalled_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(tempDir, "test.csproj"), "<Project/>");

        var (host, _, _) = WorkloadTestFactory.Create(_interaction);
        var command = new PackCommand(_interaction, host);

        try
        {
            var exitCode = await command.Parse([tempDir]).InvokeAsync();
            Assert.Equal(1, exitCode);
            Assert.Contains(_interaction.Lines, l => l.Contains("Could not determine the worker runtime"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PackCommand_HasExpectedOptions()
    {
        var (host, _, _) = WorkloadTestFactory.Create(_interaction);
        var command = new PackCommand(_interaction, host);

        var options = command.Options.Select(o => o.Name).ToList();
        Assert.Contains("--output", options);
        Assert.Contains("--no-build", options);
    }

    [Fact]
    public void PackCommand_RegisteredInParser()
    {
        var root = WorkloadTestFactory.CreateParser(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();
        Assert.Contains("pack", names);
    }

    [Fact]
    public void PackCommand_HasPathArgument()
    {
        var (host, _, _) = WorkloadTestFactory.Create(_interaction);
        var command = new PackCommand(_interaction, host);
        Assert.Contains(command.Arguments, a => a.Name == "path");
    }
}
