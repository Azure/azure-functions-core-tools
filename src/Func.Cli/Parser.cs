// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli;

/// <summary>
/// Assembles the root command tree from DI. Every top-level command —
/// built-in or workload-contributed — is resolved as a <see cref="Command"/>
/// from the container so the composition path is uniform. HelpCommand is
/// constructed inline because it needs a back-reference to the constructed
/// root.
/// </summary>
internal static class Parser
{
    /// <summary>
    /// Creates and configures the root CLI command, resolving all
    /// <see cref="Command"/> services from <paramref name="services"/>.
    /// Throws if two registered commands share a name.
    /// </summary>
    public static FuncRootCommand CreateCommand(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var interaction = services.GetRequiredService<IInteractionService>();
        var rootCommand = new FuncRootCommand();

        // HelpCommand needs the constructed root, so it can't be DI-resolved
        // ahead of the root. Built inline and added first.
        var helpCommand = new HelpCommand(interaction, rootCommand);
        rootCommand.Subcommands.Add(helpCommand);

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { helpCommand.Name };
        foreach (var command in services.GetServices<Command>())
        {
            if (!seenNames.Add(command.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate top-level command name '{command.Name}'. " +
                    "A workload registered a Command that collides with a built-in or another workload.");
            }

            rootCommand.Subcommands.Add(command);
        }

        var versionCommand = services.GetRequiredService<VersionCommand>();

        ReplaceHelpAction(rootCommand, helpCommand);
        ConfigureRootAction(rootCommand, helpCommand, versionCommand);

        return rootCommand;
    }

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
