// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli;

/// <summary>
/// Assembles the root command tree from DI. Every top-level command —
/// built-in or workload-contributed — is resolved as a <see cref="BaseCommand"/>
/// from the container so the composition path is uniform. HelpCommand is
/// constructed inline because it needs a back-reference to the constructed
/// root.
///
/// Built-in commands are tagged with <see cref="IBuiltInCommand"/>; their
/// names form the reserved set. Workload-contributed commands flow through
/// <see cref="ExternalCommand"/>, which carries the owning
/// <see cref="WorkloadInfo"/>. Workload commands whose name collides with a
/// built-in (or with another workload command) are skipped at startup with
/// a warning to stderr that names the workload, leaving the rest of the CLI
/// usable.
/// </summary>
internal static class Parser
{
    /// <summary>
    /// Creates and configures the root CLI command, resolving all
    /// <see cref="BaseCommand"/> services from <paramref name="services"/>.
    /// </summary>
    public static FuncRootCommand CreateCommand(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var interaction = services.GetRequiredService<IInteractionService>();
        var rootCommand = new FuncRootCommand();
        var versionCommand = services.GetRequiredService<VersionCommand>();

        // HelpCommand needs the constructed root, so it can't be DI-resolved
        // ahead of the root. Built inline and added first.
        var helpCommand = new HelpCommand(interaction, rootCommand, versionCommand);
        rootCommand.Subcommands.Add(helpCommand);

        var allCommands = services.GetServices<BaseCommand>().ToList();

        // Defensive: every BaseCommand resolved from DI must be either a
        // built-in or an ExternalCommand (workload-contributed). Anything
        // else is a CLI bug — fail fast at startup so it's caught in tests
        // rather than producing untraceable workload commands.
        foreach (var command in allCommands)
        {
            if (command is not IBuiltInCommand && command is not ExternalCommand)
            {
                throw new InvalidOperationException(
                    $"Top-level command '{command.GetType().FullName}' (name: '{command.Name}') is registered " +
                    "as a BaseCommand but is neither IBuiltInCommand nor ExternalCommand. " +
                    "Built-in commands must implement IBuiltInCommand; workload commands must be registered " +
                    "through FunctionsCliBuilder.RegisterCommand. This is a CLI bug.");
            }
        }

        // First pass: built-ins. Their names form the reserved set, plus
        // help (added above). Built-ins are trusted by construction — any
        // collision among them is a CLI bug, not a workload problem, so we
        // throw rather than skip.
        var reservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { helpCommand.Name };
        foreach (var command in allCommands.OfType<IBuiltInCommand>().Cast<BaseCommand>())
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
        var workloadCommands = allCommands.OfType<ExternalCommand>().ToList();
        var workloadNameGroups = workloadCommands
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var command in workloadCommands)
        {
            if (reservedNames.Contains(command.Name))
            {
                interaction.WriteWarning(
                    $"Workload '{command.Workload.PackageId}' tried to register top-level command " +
                    $"'{command.Name}', which is reserved by the CLI. This command was not loaded.");
                continue;
            }

            var siblings = workloadNameGroups[command.Name];
            if (siblings.Count > 1)
            {
                var workloadList = string.Join(", ", siblings
                    .Select(c => $"'{c.Workload.PackageId}'")
                    .Distinct(StringComparer.Ordinal));
                interaction.WriteWarning(
                    $"Workloads {workloadList} all registered top-level command '{command.Name}'. " +
                    "All conflicting registrations were skipped. " +
                    "Run `func workload uninstall <name>` to keep one.");
                continue;
            }

            rootCommand.Subcommands.Add(command);
        }

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
            if (parseResult.GetValue(rootCommand.VerboseOption))
            {
                return versionCommand.ExecuteDetailed();
            }

            return helpCommand.ShowGeneralHelp();
        });
    }
}

