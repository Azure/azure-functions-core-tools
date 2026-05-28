// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Replaces System.CommandLine's built-in help action on every command
/// so that --help and -h render uniform Spectre-based output.
/// Uses the HelpCommand's renderer to generate help from real Command metadata.
///
/// Acts as a small dispatcher: when the matched command needs specialised
/// rendering (e.g. <see cref="ITemplateAwareHelpProvider"/> commands need
/// dynamically-attached options grouped under their own section header),
/// the dispatch is delegated to a sibling action so this class stays free
/// of per-command rendering rules.
/// </summary>
internal sealed class SpectreHelpAction : SynchronousCommandLineAction
{
    private readonly HelpCommand _helpCommand;
    private readonly TemplateAwareHelpAction _templateAwareHelp;

    public SpectreHelpAction(HelpCommand helpCommand, TemplateAwareHelpAction templateAwareHelp)
    {
        _helpCommand = helpCommand ?? throw new ArgumentNullException(nameof(helpCommand));
        _templateAwareHelp = templateAwareHelp ?? throw new ArgumentNullException(nameof(templateAwareHelp));
    }

    public override int Invoke(ParseResult parseResult)
    {
        Command command = parseResult.CommandResult.Command;

        if (command is RootCommand)
        {
            return _helpCommand.ShowGeneralHelp();
        }

        if (command is ITemplateAwareHelpProvider)
        {
            return _templateAwareHelp.Invoke(parseResult);
        }

        _helpCommand.RenderCommandHelp(command);
        return 0;
    }
}
