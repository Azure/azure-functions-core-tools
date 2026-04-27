// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli;

/// <summary>
/// Assembles the root command tree. Keeps an explicit composition root
/// (commands are constructed here, not auto-discovered) so the command
/// tree is deterministic and easy to reason about.
/// </summary>
internal static class Parser
{
    /// <summary>
    /// Creates and configures the root CLI command with all subcommands registered.
    /// </summary>
    public static FuncRootCommand CreateCommand(IInteractionService interaction)
    {
        var rootCommand = new FuncRootCommand();

        // Create built-in commands
        var helpCommand = new HelpCommand(interaction, rootCommand);
        var versionCommand = new VersionCommand(interaction);
        var initCommand = new InitCommand(interaction);
        var newCommand = new NewCommand(interaction);
        var packCommand = new PackCommand(interaction);
        var startCommand = new StartCommand(interaction);

        // Register built-in commands
        rootCommand.Subcommands.Add(versionCommand);
        rootCommand.Subcommands.Add(helpCommand);
        rootCommand.Subcommands.Add(initCommand);
        rootCommand.Subcommands.Add(newCommand);
        rootCommand.Subcommands.Add(packCommand);
        rootCommand.Subcommands.Add(startCommand);

        // Replace built-in help rendering with Spectre on all commands
        ReplaceHelpAction(rootCommand, helpCommand);

        // Wire the root action (no-args → help, --verbose → detailed version)
        ConfigureRootAction(rootCommand, helpCommand, versionCommand);

        return rootCommand;
    }

    /// <summary>
    /// Replaces the built-in System.CommandLine help action on every command
    /// so that --help, -h, and -? all render uniform Spectre-based output.
    /// </summary>
    private static void ReplaceHelpAction(FuncRootCommand rootCommand, HelpCommand helpCommand)
    {
        var spectreHelp = new SpectreHelpAction(helpCommand);

        SetHelpAction(rootCommand, spectreHelp);

        foreach (var sub in rootCommand.Subcommands)
        {
            SetHelpAction(sub, spectreHelp);

            foreach (var nested in sub.Subcommands)
            {
                SetHelpAction(nested, spectreHelp);
            }
        }
    }

    private static void SetHelpAction(Command command, SpectreHelpAction action)
    {
        var helpOption = command.Options.OfType<HelpOption>().FirstOrDefault();
        if (helpOption is not null)
        {
            helpOption.Action = action;
        }
    }

    private static void ConfigureRootAction(
        FuncRootCommand rootCommand,
        HelpCommand helpCommand,
        VersionCommand versionCommand)
    {
        rootCommand.SetAction(parseResult =>
        {
            if (parseResult.GetValue(FuncRootCommand.VerboseOption))
            {
                return versionCommand.ExecuteDetailed();
            }

            return helpCommand.ShowGeneralHelp();
        });
    }
}
