// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Help action for commands that implement <see cref="ITemplateAwareHelpProvider"/>
/// (currently <c>func new</c>). Differs from <see cref="SpectreHelpAction"/> in two
/// ways:
///
/// <list type="number">
///   <item>It first asks the command to attach any options hydrated from a
///         pre-bound value (e.g. <c>--template &lt;id&gt;</c>), so the user
///         sees the union of built-in options and template-derived options.</item>
///   <item>It renders the hydrated options under their own
///         "Template Options (&lt;id&gt;)" section so the visual split between
///         universal options and template-specific options is obvious.</item>
/// </list>
///
/// The generic header / usage / arguments / subcommands rendering is delegated
/// to <see cref="HelpCommand"/> via its <c>Write*</c> helpers so the default
/// renderer stays the single source of truth for layout.
/// </summary>
internal sealed class TemplateAwareHelpAction : SynchronousCommandLineAction
{
    private readonly HelpCommand _helpCommand;

    public TemplateAwareHelpAction(HelpCommand helpCommand)
    {
        _helpCommand = helpCommand ?? throw new ArgumentNullException(nameof(helpCommand));
    }

    public override int Invoke(ParseResult parseResult)
    {
        Command command = parseResult.CommandResult.Command;

        if (command is not ITemplateAwareHelpProvider templateAware)
        {
            // Defensive fallback: a non-template-aware command should never
            // be wired up to this action, but if it is, render the standard
            // help instead of crashing.
            _helpCommand.RenderCommandHelp(command);
            return 0;
        }

        // Attach any options the bound value implies (e.g. the prompts for
        // the chosen template) so they appear in the rendered list. A
        // resolution failure here is non-fatal — the renderer just shows
        // built-ins.
        templateAware.AttachHydratedOptionsForHelp(parseResult);

        string commandPath = HelpCommand.BuildCommandPath(command);

        _helpCommand.WriteHeader(commandPath, command.Description);
        _helpCommand.WriteUsageSection(command, commandPath);
        _helpCommand.WriteArgumentsSection(command);
        _helpCommand.WriteSubcommandsSection(command);

        WriteOptionSections(command, templateAware);

        return 0;
    }

    /// <summary>
    /// Renders the options block as either a single "Options" section (when
    /// no template-hydrated options are attached) or as two sections — the
    /// built-in "Options" plus a "Template Options (&lt;id&gt;)" block — so
    /// the user can tell at a glance which options exist for every template
    /// vs which exist only because of their <c>--template</c> choice.
    /// </summary>
    private void WriteOptionSections(Command command, ITemplateAwareHelpProvider templateAware)
    {
        var allOptions = command.Options.Where(o => !o.Hidden && o is not HelpOption).ToList();
        TemplateHelpInfo? templateHelp = templateAware.GetTemplateHelpInfo();

        if (templateHelp is null || templateHelp.HydratedOptions.Count == 0)
        {
            if (allOptions.Count > 0)
            {
                _helpCommand.WriteOptionsSection("Options", allOptions);
            }

            return;
        }

        var hydratedNames = new HashSet<string>(
            templateHelp.HydratedOptions.Select(o => o.Name),
            StringComparer.Ordinal);

        var builtIn = allOptions.Where(o => !hydratedNames.Contains(o.Name)).ToList();
        if (builtIn.Count > 0)
        {
            _helpCommand.WriteOptionsSection("Options", builtIn);
        }

        _helpCommand.WriteOptionsSection(
            $"Template Options ({templateHelp.TemplateId})",
            templateHelp.HydratedOptions);
    }
}
