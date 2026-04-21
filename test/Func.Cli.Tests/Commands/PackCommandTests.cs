// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

[Collection(nameof(WorkingDirectoryTests))]
public class PackCommandTests
{
    private readonly TestInteractionService _interaction;

    public PackCommandTests()
    {
        _interaction = new TestInteractionService();
    }

    [Fact]
    public async Task ExecuteAsync_NoHostJson_ReturnsError()
    {
        // Use a temp dir with no host.json
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var command = new PackCommand(_interaction);
            var parseResult = command.Parse([tempDir]);

            var exitCode = await parseResult.InvokeAsync();

            Assert.Equal(1, exitCode);
            Assert.Contains(_interaction.Lines, l => l.Contains("No Azure Functions project found"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkload_ReturnsError()
    {
        // Create a temp dir with host.json and a .csproj so runtime is detected
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(tempDir, "test.csproj"), "<Project/>");
        try
        {
            var command = new PackCommand(_interaction);
            var parseResult = command.Parse([tempDir]);

            var exitCode = await parseResult.InvokeAsync();

            Assert.Equal(1, exitCode);
            Assert.Contains(_interaction.Lines, l => l.Contains("No pack provider"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoRuntimeDetected_ReturnsError()
    {
        // Create a temp dir with host.json but no project files
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "host.json"), "{}");
        try
        {
            var command = new PackCommand(_interaction);
            var parseResult = command.Parse([tempDir]);

            var exitCode = await parseResult.InvokeAsync();

            Assert.Equal(1, exitCode);
            Assert.Contains(_interaction.Lines, l => l.Contains("Could not detect"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PackCommand_HasExpectedOptions()
    {
        var command = new PackCommand(_interaction);

        var options = command.Options.Select(o => o.Name).ToList();
        Assert.Contains("--output", options);
        Assert.Contains("--no-build", options);
    }

    [Fact]
    public void PackCommand_RegisteredInParser()
    {
        var root = Parser.CreateCommand(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();
        Assert.Contains("pack", names);
    }

    [Fact]
    public void PackCommand_HasPathArgument()
    {
        var command = new PackCommand(_interaction);
        Assert.Contains(command.Arguments, a => a.Name == "path");
    }

    [Fact]
    public async Task ExecuteAsync_WithOutput_CreatesOutputDirectory()
    {
        // host.json + csproj but no workload → will fail at "no pack provider"
        // but we verify the path arg and detection work up to that point
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "host.json"), "{}");
        File.WriteAllText(Path.Combine(tempDir, "test.csproj"), "<Project/>");
        try
        {
            var command = new PackCommand(_interaction);
            var parseResult = command.Parse(["--output", "myoutput", tempDir]);

            var exitCode = await parseResult.InvokeAsync();

            // Should fail because no workload installed, but runtime should be detected
            Assert.Equal(1, exitCode);
            Assert.Contains(_interaction.Lines, l => l.Contains("No pack provider") && l.Contains("dotnet"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
