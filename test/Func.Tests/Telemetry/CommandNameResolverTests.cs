// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Telemetry;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class CommandNameResolverTests
{
    [Fact]
    public void ResolveCommandName_NullParseResult_Throws()
    {
        var rootCommand = new FuncRootCommand();

        FluentActions.Invoking(() => CommandNameResolver.ResolveCommandName(null!, rootCommand)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void ResolveCommandName_NullRootCommand_Throws()
    {
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse([]);

        FluentActions.Invoking(() => CommandNameResolver.ResolveCommandName(parseResult, null!)).Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void ResolveCommandName_NoArgs_ReturnsHelp()
    {
        // `func` alone → root action shows help.
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse([]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        name.Should().Be("help");
    }

    [Fact]
    public void ResolveCommandName_VerboseFlagOnly_ReturnsVersion()
    {
        // `func --verbose` → root action runs detailed version output.
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse(["--verbose"]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        name.Should().Be("version");
    }

    [Fact]
    public void ResolveCommandName_TopLevelSubcommand_ReturnsCommandName()
    {
        var rootCommand = new FuncRootCommand();
        var initCommand = new Command("init", "init description");
        rootCommand.Subcommands.Add(initCommand);
        ParseResult parseResult = rootCommand.Parse(["init"]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        name.Should().Be("init");
    }

    [Fact]
    public void ResolveCommandName_NestedSubcommand_ReturnsJoinedPath()
    {
        var rootCommand = new FuncRootCommand();
        var workloadCommand = new Command("workload", "workload description");
        var listCommand = new Command("list", "list description");
        workloadCommand.Subcommands.Add(listCommand);
        rootCommand.Subcommands.Add(workloadCommand);
        ParseResult parseResult = rootCommand.Parse(["workload", "list"]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        name.Should().Be("workload list");
    }

    [Fact]
    public void ResolveCommandName_DeeplyNestedSubcommand_ReturnsFullPath()
    {
        // Defensive: ensure the walk isn't artificially capped at two levels.
        var rootCommand = new FuncRootCommand();
        var workloadCommand = new Command("workload", "workload description");
        var remoteCommand = new Command("remote", "remote description");
        var addCommand = new Command("add", "add description");
        remoteCommand.Subcommands.Add(addCommand);
        workloadCommand.Subcommands.Add(remoteCommand);
        rootCommand.Subcommands.Add(workloadCommand);
        ParseResult parseResult = rootCommand.Parse(["workload", "remote", "add"]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        name.Should().Be("workload remote add");
    }

    [Fact]
    public void ResolveCommandName_VerboseAfterSubcommand_ReturnsSubcommandName()
    {
        // `func init --verbose` is still an `init` invocation; the recursive
        // verbose flag must not promote the activity to "version".
        var rootCommand = new FuncRootCommand();
        var initCommand = new Command("init", "init description");
        rootCommand.Subcommands.Add(initCommand);
        ParseResult parseResult = rootCommand.Parse(["init", "--verbose"]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        name.Should().Be("init");
    }

    [Fact]
    public void ResolveCommandName_UnknownToken_ReturnsUnknown()
    {
        // `func badcommand` produces a parse error and stays at root —
        // we should not telemeter typos as "help".
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse(["badcommand"]);

        parseResult.Errors.Should().NotBeEmpty();

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        name.Should().Be("unknown");
    }
}
