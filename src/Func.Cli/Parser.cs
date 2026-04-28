// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli;

/// <summary>
/// Assembles the root command tree from DI. Every top-level command —
/// built-in or workload-contributed — is resolved as a <see cref="Command"/>
/// from the container so the composition path is uniform. HelpCommand is
/// constructed inline because it needs a back-reference to the constructed
/// root.
///
/// Built-in commands are tagged with <see cref="IBuiltInCommand"/>; their
/// names form the reserved set. Workload-contributed commands whose name
/// collides with a built-in (or with another workload command) are skipped
/// at startup with a warning to stderr, leaving the rest of the CLI usable.
/// </summary>
internal static class Parser
{
    /// <summary>
    /// Creates and configures the root CLI command, resolving all
    /// <see cref="Command"/> services from <paramref name="services"/>.
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

        var allCommands = services.GetServices<Command>().ToList();

        // First pass: built-ins. Their names form the reserved set, plus
        // help (added above). Built-ins are trusted by construction — any
        // collision among them is a CLI bug, not a workload problem, so we
        // throw rather than skip.
        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { helpCommand.Name };
        foreach (var command in allCommands.OfType<IBuiltInCommand>().Cast<Command>())
        {
            if (!reservedNames.Add(command.Name))
            {
                throw new InvalidOperationException(
                    $"Two built-in commands share the name '{command.Name}'. This is a CLI bug.");
            }

            rootCommand.Subcommands.Add(command);
        }

        // Second pass: workload-contributed commands. A command that
        // collides with a built-in is skipped (built-in wins). Two workload
        // commands colliding with each other are both skipped — picking one
        // would be non-deterministic from the user's perspective.
        var workloadCommands = allCommands.Where(c => c is not IBuiltInCommand).ToList();
        var workloadNameCounts = workloadCommands
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var command in workloadCommands)
        {
            if (reservedNames.Contains(command.Name))
            {
                interaction.WriteWarning(
                    $"A workload tried to register top-level command '{command.Name}', which is reserved by the CLI. This command was not loaded.");
                continue;
            }

            if (workloadNameCounts[command.Name] > 1)
            {
                interaction.WriteWarning(
                    $"Multiple workloads registered top-level command '{command.Name}'. All conflicting registrations were skipped. " +
                    "Run `func workload uninstall <name>` to keep one.");
                continue;
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
