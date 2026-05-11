// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Telemetry;
using Xunit;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class CommandNameResolverTests
{
    [Fact]
    public void ResolveCommandName_NullParseResult_Throws()
    {
        var rootCommand = new FuncRootCommand();

        Assert.Throws<ArgumentNullException>(
            () => CommandNameResolver.ResolveCommandName(null!, rootCommand));
    }

    [Fact]
    public void ResolveCommandName_NullRootCommand_Throws()
    {
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse([]);

        Assert.Throws<ArgumentNullException>(
            () => CommandNameResolver.ResolveCommandName(parseResult, null!));
    }

    [Fact]
    public void ResolveCommandName_NoArgs_ReturnsHelp()
    {
        // `func` alone → root action shows help.
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse([]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        Assert.Equal("help", name);
    }

    [Fact]
    public void ResolveCommandName_VerboseFlagOnly_ReturnsVersion()
    {
        // `func --verbose` → root action runs detailed version output.
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse(["--verbose"]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        Assert.Equal("version", name);
    }

    [Fact]
    public void ResolveCommandName_TopLevelSubcommand_ReturnsCommandName()
    {
        var rootCommand = new FuncRootCommand();
        var initCommand = new Command("init", "init description");
        rootCommand.Subcommands.Add(initCommand);
        ParseResult parseResult = rootCommand.Parse(["init"]);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        Assert.Equal("init", name);
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

        Assert.Equal("workload list", name);
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

        Assert.Equal("workload remote add", name);
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

        Assert.Equal("init", name);
    }

    [Fact]
    public void ResolveCommandName_UnknownToken_ReturnsUnknown()
    {
        // `func badcommand` produces a parse error and stays at root —
        // we should not telemeter typos as "help".
        var rootCommand = new FuncRootCommand();
        ParseResult parseResult = rootCommand.Parse(["badcommand"]);

        Assert.NotEmpty(parseResult.Errors);

        string name = CommandNameResolver.ResolveCommandName(parseResult, rootCommand);

        Assert.Equal("unknown", name);
    }
}
