// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Commands;

namespace Azure.Functions.Cli.Tests.Commands;

public class NewCommandTests
{
    private readonly TestInteractionService _interaction;

    public NewCommandTests()
    {
        _interaction = new TestInteractionService();
    }

    [Fact]
    public void NewCommand_HasExpectedOptions()
    {
        var cmd = new NewCommand(_interaction);
        var optionNames = cmd.Options.Select(o => o.Name).ToList();

        optionNames.Should().Contain("--name");
        optionNames.Should().Contain("--template");
        optionNames.Should().Contain("--force");
        optionNames.Should().Contain("--non-interactive");
        optionNames.Should().Contain("--list");
        optionNames.Should().Contain("--format");
        optionNames.Should().NotContain("--output");
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("json")]
    public async Task NewCommand_List_FailsWithMigrationDiagnostic(string format)
    {
        var command = new NewCommand(_interaction);
        ParseResult result = command.Parse(["--list", "--format", format]);

        int exitCode = await result.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

        exitCode.Should().Be(1);
        _interaction.AllOutput.Should().Contain("temporarily unavailable");
    }

    [Theory]
    [InlineData("--output", "json")]
    [InlineData("--output=json")]
    [InlineData("--output:json")]
    public void NewCommand_RemovedOutputOption_ReportsUnrecognizedOption(params string[] args)
    {
        var command = new NewCommand(_interaction);
        ParseResult result = command.Parse(["--list", .. args]);

        result.Errors.Should().Contain(error =>
            error.Message.Contains("Unrecognized", StringComparison.Ordinal)
            && error.Message.Contains("--output", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--output", "json")]
    [InlineData("--output=json")]
    [InlineData("--output:json")]
    public void NewCommand_RemovedOutputOptionWithExplicitPath_ReportsUnrecognizedOption(params string[] args)
    {
        var command = new NewCommand(_interaction);
        ParseResult result = command.Parse([".", "--list", .. args]);

        result.Errors.Should().Contain(error =>
            error.Message.Contains("Unrecognized", StringComparison.Ordinal)
            && error.Message.Contains("--output", StringComparison.Ordinal));
    }

    [Fact]
    public void NewCommand_RegisteredInParser()
    {
        var root = TestParser.CreateRoot(_interaction);
        var names = root.Subcommands.Select(c => c.Name).ToList();

        names.Should().Contain("new");
    }

    [Fact]
    public void NewCommand_HasPathArgument()
    {
        var cmd = new NewCommand(_interaction);
        cmd.Arguments.Should().ContainSingle();
        cmd.Arguments[0].Name.Should().Be("path");
    }

    // Single-dash typos like `-name` must surface as unrecognized options, not "needs a project".
    [Fact]
    public void NewCommand_SingleDashLongOption_ReportsUnrecognizedOption()
    {
        var root = TestParser.CreateRoot(_interaction);

        ParseResult result = root.Parse(
            new[] { "new", "--template", "HttpTrigger-Python", "-name", "ttpt" },
            new ParserConfiguration { EnablePosixBundling = false });

        result.Errors.Should().Contain(e => e.Message.Contains("Unrecognized option '-name'", System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NewCommand_Unavailable_LeavesTargetUnchanged(bool exists, bool force)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"func-new-unavailable-{Guid.NewGuid():N}");
        string configDirectory = Path.Combine(directory, ".func");
        string configPath = Path.Combine(configDirectory, "config.json");
        const string config = """{"stack":{"runtime":"node"},"custom":"keep"}""";
        if (exists)
        {
            Directory.CreateDirectory(configDirectory);
            await File.WriteAllTextAsync(configPath, config);
            await File.WriteAllTextAsync(Path.Combine(directory, "host.json"), "{}");
            Directory.CreateDirectory(Path.Combine(directory, "empty"));
        }

        try
        {
            var command = new NewCommand(_interaction);
            string[] args = [directory, "--template", "HttpTrigger", "--name", "Example", "--non-interactive"];
            if (force)
            {
                args = [.. args, "--force"];
            }

            int exitCode = await command.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

            exitCode.Should().Be(1);
            _interaction.AllOutput.Should().Contain("temporarily unavailable");
            Directory.Exists(directory).Should().Be(exists);
            if (exists)
            {
                Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                    .Should().BeEquivalentTo([configDirectory, configPath, Path.Combine(directory, "host.json"), Path.Combine(directory, "empty")]);
                (await File.ReadAllTextAsync(configPath)).Should().Be(config);
                (await File.ReadAllTextAsync(Path.Combine(directory, "host.json"))).Should().Be("{}");
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void NewCommand_NullInteraction_Throws()
    {
        Action act = () => _ = new NewCommand(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("interaction");
    }
}
