// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Invocation;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Replaces System.CommandLine's built-in help action on every command
/// so that --help and -h render uniform Spectre-based output.
/// Uses the HelpCommand's renderer to generate help from real Command metadata.
/// </summary>
internal sealed class SpectreHelpAction(HelpCommand helpCommand) : SynchronousCommandLineAction
{
    private readonly HelpCommand _helpCommand = helpCommand ?? throw new ArgumentNullException(nameof(helpCommand));

    public override int Invoke(ParseResult parseResult)
    {
        Command command = parseResult.CommandResult.Command;

        if (command is RootCommand)
        {
            return _helpCommand.ShowGeneralHelp();
        }

        // Stage-B help hydration: when the matched command is a
        // template-aware verb (e.g. `func new`) and the user supplied a
        // stage-A value the help renderer needs to know about
        // (`--template <id>`), let the command attach the relevant
        // dynamic options before we hand it to the renderer. Falls
        // through to a built-ins-only render when the hook returns 0.
        if (command is ITemplateAwareHelpProvider templateAware)
        {
            templateAware.AttachHydratedOptionsForHelp(parseResult);
        }

        _helpCommand.RenderCommandHelp(command);
        return 0;
    }
}
