// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Renders rich help generated from real <see cref="Command"/> metadata.
/// </summary>
internal class HelpCommand : BaseCommand
{
    public Argument<string?> CommandArgument { get; } = new("command")
    {
        Description = "The command to get help for.",
        Arity = ArgumentArity.ZeroOrOne
    };

    private readonly IInteractionService _interaction;
    private readonly FuncRootCommand _rootCommand;
    private readonly VersionCommand _versionCommand;

    public HelpCommand(IInteractionService interaction, FuncRootCommand rootCommand, VersionCommand versionCommand)
        : base("help", "Show help information for Azure Functions CLI.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(rootCommand);
        ArgumentNullException.ThrowIfNull(versionCommand);
        Hidden = true;
        _interaction = interaction;
        _rootCommand = rootCommand;
        _versionCommand = versionCommand;
        Arguments.Add(CommandArgument);
    }

    protected override Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        var commandName = parseResult.GetValue(CommandArgument);

        return Task.FromResult(string.IsNullOrEmpty(commandName)
            ? ShowGeneralHelp()
            : ShowCommandHelp(commandName));
    }

    /// <summary>Shows top-level help: product banner, command list, global options.</summary>
    internal int ShowGeneralHelp()
    {
        var version = _versionCommand.GetVersion();

        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Title(Constants.ProductName)
            .Plain(" ")
            .Muted($"({version})"));
        _interaction.WriteBlankLine();
        _interaction.WriteHint("Create, develop, test, and deploy Azure Functions from the command line.");
        _interaction.WriteBlankLine();

        _interaction.WriteSectionHeader("Usage");
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l
            .Plain("  ")
            .Command("func ")
            .Placeholder("<command> ")
            .OptionalArg("[path] ")
            .Muted("[options]"));
        _interaction.WriteBlankLine();

        var commands = _rootCommand.Subcommands
            .Where(c => !c.Hidden)
            .Select(c => new DefinitionItem(c.Name, c.Description ?? string.Empty))
            .ToList();
        if (commands.Count > 0)
        {
            _interaction.WriteSectionHeader("Commands");
            _interaction.WriteBlankLine();
            _interaction.WriteDefinitionList(commands);
            _interaction.WriteBlankLine();
        }

        _interaction.WriteSectionHeader("Global Options");
        _interaction.WriteBlankLine();
        var options = _rootCommand.Options
            .Where(o => o is not HelpOption && o.Name != "--version")
            .Select(o => new DefinitionItem(FormatOptionName(o), o.Description ?? string.Empty))
            .Append(new DefinitionItem("--help, -h, -?", "Show help information"))
            .Append(new DefinitionItem("--version", "Display the current version"));
        _interaction.WriteDefinitionList(options);
        _interaction.WriteBlankLine();

        _interaction.WriteLine(l => l.Muted($"Documentation: {Constants.DocsUrl}"));
        _interaction.WriteBlankLine();

        return 0;
    }

    /// <summary>Shows help for a named subcommand.</summary>
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
    /// Renders help for any <see cref="Command"/>. Used by both <c>func help &lt;command&gt;</c>
    /// and the global <c>--help</c> / <c>-h</c> / <c>-?</c> handler.
    /// </summary>
    internal void RenderCommandHelp(Command command)
    {
        var isRoot = command is RootCommand;
        var commandPath = isRoot ? "func" : $"func {command.Name}";

        _interaction.WriteBlankLine();
        _interaction.WriteTitle(commandPath);
        _interaction.WriteBlankLine();

        if (!string.IsNullOrEmpty(command.Description))
        {
            _interaction.WriteHint(command.Description);
            _interaction.WriteBlankLine();
        }

        _interaction.WriteSectionHeader("Usage");
        _interaction.WriteBlankLine();
        WriteUsageLine(command, commandPath);
        _interaction.WriteBlankLine();

        var args = command.Arguments.Where(a => !a.Hidden).ToList();
        if (args.Count > 0)
        {
            _interaction.WriteSectionHeader("Arguments");
            _interaction.WriteBlankLine();
            _interaction.WriteDefinitionList(
                args.Select(a => new DefinitionItem($"<{a.Name}>", a.Description ?? string.Empty)));
            _interaction.WriteBlankLine();
        }

        var subcommands = command.Subcommands.Where(c => !c.Hidden).ToList();
        if (subcommands.Count > 0)
        {
            _interaction.WriteSectionHeader("Commands");
            _interaction.WriteBlankLine();
            _interaction.WriteDefinitionList(
                subcommands.Select(c => new DefinitionItem(c.Name, c.Description ?? string.Empty)));
            _interaction.WriteBlankLine();
        }

        var options = command.Options.Where(o => !o.Hidden && o is not HelpOption).ToList();
        if (options.Count > 0)
        {
            _interaction.WriteSectionHeader("Options");
            _interaction.WriteBlankLine();
            _interaction.WriteDefinitionList(
                options.Select(o => new DefinitionItem(FormatOptionName(o), o.Description ?? string.Empty)));
            _interaction.WriteBlankLine();
        }
    }

    /// <summary>
    /// Writes the usage line for a command, composing each token with its
    /// semantic style (command, placeholder, optional arg, options hint).
    /// </summary>
    private void WriteUsageLine(Command command, string commandPath)
    {
        _interaction.WriteLine(line =>
        {
            line.Plain("  ").Command(commandPath);

            foreach (var arg in command.Arguments.Where(a => !a.Hidden))
            {
                line.Plain(" ");
                if (arg.Arity.MinimumNumberOfValues > 0)
                {
                    line.Placeholder($"<{arg.Name}>");
                }
                else
                {
                    line.OptionalArg($"[{arg.Name}]");
                }
            }

            if (command.Subcommands.Any(c => !c.Hidden))
            {
                line.Plain(" ").Placeholder("<command>");
            }

            if (command.Options.Any(o => !o.Hidden && o is not HelpOption))
            {
                line.Plain(" ").Muted("[options]");
            }
        });
    }

    private string FormatOptionName(Option option)
    {
        var names = new List<string> { option.Name };
        names.AddRange(option.Aliases.Where(a => a != option.Name));
        return string.Join(", ", names);
    }
}
