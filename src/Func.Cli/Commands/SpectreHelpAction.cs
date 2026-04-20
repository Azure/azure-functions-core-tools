// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Replaces System.CommandLine's built-in help action on every command
/// so that --help, -h, and -? all render uniform Spectre-based output.
/// Uses the HelpCommand's renderer to generate help from real Command metadata.
/// </summary>
internal sealed class SpectreHelpAction : SynchronousCommandLineAction
{
    private readonly HelpCommand _helpCommand;

    public SpectreHelpAction(HelpCommand helpCommand)
    {
        _helpCommand = helpCommand;
    }

    public override int Invoke(ParseResult parseResult)
    {
        var command = parseResult.CommandResult.Command;

        if (command is RootCommand)
        {
            return _helpCommand.ShowGeneralHelp();
        }

        _helpCommand.RenderCommandHelp(command);
        return 0;
    }
}
