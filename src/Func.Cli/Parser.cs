// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Cli;

/// <summary>
/// Assembles the root command tree from DI. Built-in commands are still
/// constructed explicitly here (composition root) so the tree is deterministic;
/// workloads contribute either via DI (consumed by built-in commands) or via
/// <see cref="ICommandProvider"/> for entirely new subcommands.
/// </summary>
public static class Parser
{
    /// <summary>
    /// Convenience overload for tests/scenarios that don't need workloads —
    /// builds a minimal service provider with just the supplied interaction service.
    /// </summary>
    public static FuncRootCommand CreateCommand(IInteractionService interaction)
    {
        var services = new ServiceCollection();
        services.AddSingleton(interaction);
        services.AddSingleton<IReadOnlyList<InstalledWorkload>>(Array.Empty<InstalledWorkload>());
        return CreateCommand(services.BuildServiceProvider());
    }

    /// <summary>
    /// Creates and configures the root CLI command, resolving built-in commands
    /// from <paramref name="services"/> and inviting workload providers to
    /// add their own subcommands.
    /// </summary>
    public static FuncRootCommand CreateCommand(IServiceProvider services)
    {
        var interaction = services.GetRequiredService<IInteractionService>();
        var rootCommand = new FuncRootCommand();

        var helpCommand = new HelpCommand(interaction, rootCommand);
        var versionCommand = new VersionCommand(interaction);
        var initCommand = ActivatorUtilities.CreateInstance<InitCommand>(services);
        var newCommand = new NewCommand(interaction);
        var packCommand = new PackCommand(interaction);
        var startCommand = new StartCommand(interaction);

        var workloadListCommand = ActivatorUtilities.CreateInstance<WorkloadListCommand>(services);
        var workloadInstallCommand = new WorkloadInstallCommand(interaction);
        var workloadUninstallCommand = new WorkloadUninstallCommand(interaction);
        var workloadCommand = new WorkloadCommand(workloadListCommand, workloadInstallCommand, workloadUninstallCommand);

        rootCommand.Subcommands.Add(versionCommand);
        rootCommand.Subcommands.Add(helpCommand);
        rootCommand.Subcommands.Add(initCommand);
        rootCommand.Subcommands.Add(newCommand);
        rootCommand.Subcommands.Add(packCommand);
        rootCommand.Subcommands.Add(startCommand);
        rootCommand.Subcommands.Add(workloadCommand);

        // Let feature workloads add brand-new subcommands.
        foreach (var provider in services.GetServices<ICommandProvider>())
        {
            provider.Provide(rootCommand);
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
            if (parseResult.GetValue(FuncRootCommand.VerboseOption))
            {
                return versionCommand.ExecuteDetailed();
            }

            return helpCommand.ShowGeneralHelp();
        });
    }
}
