// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Spectre.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Displays rich help information using Spectre.Console. Generates help from
/// real Command objects — no separate source of truth for command metadata.
/// </summary>
public class HelpCommand : BaseCommand
{
    public static readonly Argument<string?> CommandArgument = new("command")
    {
        Description = "The command to get help for.",
        Arity = ArgumentArity.ZeroOrOne
    };

    private readonly IInteractionService _interaction;
    private readonly FuncRootCommand _rootCommand;

    public HelpCommand(IInteractionService interaction, FuncRootCommand rootCommand)
        : base("help", "Show help information for Azure Functions CLI.")
    {
        Hidden = true;
        _interaction = interaction;
        _rootCommand = rootCommand;
        Arguments.Add(CommandArgument);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var commandName = parseResult.GetValue(CommandArgument);

        if (!string.IsNullOrEmpty(commandName))
        {
            return Task.FromResult(ShowCommandHelp(commandName));
        }

        return Task.FromResult(ShowGeneralHelp());
    }

    /// <summary>
    /// Shows the top-level help with all available commands and global options.
    /// </summary>
    internal int ShowGeneralHelp()
    {
        var version = VersionCommand.GetVersion();

        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine($"[bold blue]{Constants.ProductName}[/] [dim]({version})[/]");
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("[grey]Create, develop, test, and deploy Azure Functions from the command line.[/]");
        _interaction.WriteBlankLine();

        _interaction.WriteRule("Usage");
        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("  [white]func[/] [green]<command>[/] [cyan][[path]][/] [grey][[options]][/]");
        _interaction.WriteBlankLine();

        // Generate command list from real registered commands
        var commands = _rootCommand.Subcommands
            .Where(c => !c.Hidden)
            .Select(c => (c.Name, c.Description ?? string.Empty))
            .ToList();
        if (commands.Count > 0)
        {
            _interaction.WriteRule("Commands");
            _interaction.WriteBlankLine();
            WriteAlignedList(commands);
            _interaction.WriteBlankLine();
        }

        // Generate global options from real root command options
        _interaction.WriteRule("Global Options");
        _interaction.WriteBlankLine();
        var globalOptions = _rootCommand.Options
            .Where(o => o is not HelpOption && o.Name != "--version")
            .Select(o => (FormatOptionName(o), o.Description ?? string.Empty));
        // Always include --help and --version in the display (with our formatting)
        var allOptions = globalOptions
            .Append(("--help, -h, -?", "Show help information"))
            .Append(("--version", "Display the current version"));
        WriteAlignedList(allOptions);
        _interaction.WriteBlankLine();

        _interaction.WriteMarkupLine($"[grey]Documentation: {Constants.DocsUrl}[/]");
        _interaction.WriteBlankLine();

        _interaction.WriteMarkupLine("[yellow bold]💡[/] [white]Extend the CLI with workloads for Durable Functions, Kubernetes, and more.[/]");
        _interaction.WriteMarkupLine("[white]   Run[/] [blue]func workload search[/] [white]to explore available workloads.[/]");
        _interaction.WriteBlankLine();

        return 0;
    }

    /// <summary>
    /// Shows help for a specific named command, generated from the command's metadata.
    /// </summary>
    internal int ShowCommandHelp(string commandName)
    {
        var command = _rootCommand.Subcommands
            .FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

        if (command is null)
        {
            _interaction.WriteError($"Unknown command: '{commandName}'. Run 'func help' for available commands.");
            return 1;
        }

        RenderCommandHelp(command);
        return 0;
    }

    /// <summary>
    /// Renders help for any System.CommandLine Command, used by both
    /// 'func help <command>' and the SpectreHelpAction (--help/-h/-?).
    /// </summary>
    internal void RenderCommandHelp(Command command)
    {
        var isRoot = command is RootCommand;
        var commandPath = isRoot ? "func" : BuildCommandPath(command);

        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine($"[bold blue]{commandPath}[/]");
        _interaction.WriteBlankLine();

        if (!string.IsNullOrEmpty(command.Description))
        {
            _interaction.WriteMarkupLine($"[grey]{command.Description.EscapeMarkup()}[/]");
            _interaction.WriteBlankLine();
        }

        // Usage
        _interaction.WriteRule("Usage");
        _interaction.WriteBlankLine();
        var usage = BuildUsageString(command, commandPath);
        _interaction.WriteMarkupLine($"  {usage}");
        _interaction.WriteBlankLine();

        // Arguments
        var args = command.Arguments.Where(a => !a.Hidden).ToList();
        if (args.Count > 0)
        {
            _interaction.WriteRule("Arguments");
            _interaction.WriteBlankLine();
            WriteAlignedList(args.Select(a => ($"<{a.Name}>", a.Description ?? string.Empty)));
            _interaction.WriteBlankLine();
        }

        // Subcommands
        var subcommands = command.Subcommands.Where(c => !c.Hidden).ToList();
        if (subcommands.Count > 0)
        {
            _interaction.WriteRule("Commands");
            _interaction.WriteBlankLine();
            WriteAlignedList(subcommands.Select(c => (c.Name, c.Description ?? string.Empty)));
            _interaction.WriteBlankLine();
        }

        // Options (exclude the built-in help option to avoid clutter)
        var options = command.Options.Where(o => !o.Hidden && o is not HelpOption).ToList();
        if (options.Count > 0)
        {
            _interaction.WriteRule("Options");
            _interaction.WriteBlankLine();
            WriteAlignedList(options.Select(o => (FormatOptionName(o), o.Description ?? string.Empty)));
            _interaction.WriteBlankLine();
        }
    }

    private static string BuildUsageString(Command command, string commandPath)
    {
        var parts = new List<string> { $"[white]{commandPath}[/]" };

        foreach (var arg in command.Arguments.Where(a => !a.Hidden))
        {
            var argStr = arg.Arity.MinimumNumberOfValues > 0
                ? $"<{arg.Name}>"
                : $"[[{arg.Name}]]";
            parts.Add($"[cyan]{argStr}[/]");
        }

        if (command.Subcommands.Any(c => !c.Hidden))
        {
            parts.Add("[green]<command>[/]");
        }

        if (command.Options.Any(o => !o.Hidden && o is not HelpOption))
        {
            parts.Add("[grey][[options]][/]");
        }

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Walks up the parent chain to build the full command path (e.g., "func host use").
    /// </summary>
    private static string BuildCommandPath(Command command)
    {
        var segments = new List<string>();
        var current = command;

        while (current is not null and not RootCommand)
        {
            segments.Add(current.Name);
            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        segments.Add("func");
        segments.Reverse();
        return string.Join(" ", segments);
    }

    private static string FormatOptionName(Option option)
    {
        var names = new List<string> { option.Name };
        names.AddRange(option.Aliases.Where(a => a != option.Name));
        return string.Join(", ", names);
    }

    private void WriteAlignedList(IEnumerable<(string Label, string Description)> items)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0) return;

        int maxLabelWidth = itemList.Max(i => Markup.Remove(i.Label).Length);
        int padding = maxLabelWidth + 4;

        foreach (var (label, description) in itemList)
        {
            int plainLength = Markup.Remove(label).Length;
            string gap = new(' ', padding - plainLength);
            _interaction.WriteMarkupLine($"  [green]{label.EscapeMarkup()}[/]{gap}[grey]{description.EscapeMarkup()}[/]");
        }
    }
}
