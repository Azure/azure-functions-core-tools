// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Builds the compact dashboard help overlay.
/// </summary>
internal sealed class CompactHelpOverlayBuilder(ITheme theme)
{
    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));

    private string CommandTag => field ??= _theme.Command.ToMarkup();
    private string MutedTag => field ??= _theme.Muted.ToMarkup();

    public IRenderable Build()
    {
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(3).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(0));

        AddHelpRow(table, "?", "Toggle this help panel.");
        AddHelpRow(table, "t", "Open the function browser.");
        AddHelpRow(table, "/", "Search functions by name, trigger, or route.");
        AddHelpRow(table, "↑/↓", "Move selection in the function browser.");
        AddHelpRow(table, "PgUp/PgDn", "Scroll logs; in the function browser, jump through functions.");
        AddHelpRow(table, "Home/End", "Jump to oldest logs / latest logs, or first / last function in the browser.");
        AddHelpRow(table, "Enter", "Filter logs to the selected function in the function browser.");
        AddHelpRow(table, "a", "Clear the active function filter.");
        AddHelpRow(table, "f", "Cycle the active function filter.");
        AddHelpRow(table, "c", "Clear visible log output.");
        AddHelpRow(table, "e", "Toggle errors-only log view.");
        AddHelpRow(table, "1/2/3", "Set visible log level: info, warning, or error.");
        AddHelpRow(table, "q", "Stop the host.");
        AddHelpRow(table, "Esc", "Close the active overlay.");
        AddHelpRow(table, "Ctrl+C", "Stop the host.");

        return new Panel(new Rows(
            new Markup($"[{MutedTag}]Available compact-mode controls[/]"),
            table))
        {
            Header = new PanelHeader("Help"),
            Border = BoxBorder.Rounded,
            BorderStyle = _theme.Muted,
            Expand = true,
        };
    }

    private void AddHelpRow(Table table, string key, string description)
    {
        table.AddRow(
            new Markup($"[{CommandTag}]{Markup.Escape(key)}[/]"),
            new Markup($"[{MutedTag}]{Markup.Escape(description)}[/]"));
    }
}
