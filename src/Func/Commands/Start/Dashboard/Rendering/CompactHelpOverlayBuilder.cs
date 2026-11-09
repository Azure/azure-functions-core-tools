// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Builds the compact dashboard help overlay.
/// </summary>
internal sealed class CompactHelpOverlayBuilder(ITheme theme, CompactDashboardShortcutLabels shortcutLabels, DashboardRunInfo runInfo)
{
    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    private readonly CompactDashboardShortcutLabels _shortcutLabels = shortcutLabels ?? throw new ArgumentNullException(nameof(shortcutLabels));
    private readonly DashboardRunInfo _runInfo = runInfo ?? throw new ArgumentNullException(nameof(runInfo));

    private string CommandTag => field ??= _theme.Command.ToMarkup();
    private string MutedTag => field ??= _theme.Muted.ToMarkup();

    public IRenderable Build(int width, string? status = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(3).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(0));

        void Add(string key, string description, string compact)
            => AddHelpRow(table, key, width < 80 ? compact : description);

        Add("?", "Toggle this help panel.", "Toggle help");
        Add("t", "Open the function browser.", "Browse functions");
        Add("/", "Search functions by name, trigger, or route.", "Search functions");
        Add("↑/↓", "Scroll logs; move selection in an overlay.", "Scroll / select");
        Add("Mouse wheel", "Scroll the log pane.", "Scroll logs");
        Add("Drag", "Select log text; Ctrl+C copies.", "Select text");
        Add(_shortcutLabels.PageNavigationHelpKey, "Page through logs or functions.", "Page");
        Add("Home/End", "Go to oldest/latest logs or first/last function.", "Oldest / latest");
        Add("Enter", "Filter logs to the selected function.", "Filter");
        Add("a", "Clear the function filter.", "Clear filter");
        Add("f", "Cycle the function filter.", "Next filter");
        Add("c", "Clear visible logs; keep file and counters.", "Clear logs");
        Add("e", "Toggle errors-only logs.", "Errors only");
        Add("1/2/3", "Minimum log level: info / warning / error.", "Info / warn / error");
        Add("q / Ctrl+C", "Stop host; Ctrl+C copies an active selection.", "Quit / copy");
        Add("Esc", "Close overlay or clear selection.", "Close / clear");
        Add("m", "Advanced: terminal selection; use terminal's Copy command.", "Advanced selection");

        List<IRenderable> content = [];
        if (!string.IsNullOrEmpty(status))
        {
            content.Add(new Text(LogText.Sanitize(status), _theme.Emphasis));
        }
        content.Add(new Markup($"[{MutedTag}]Available compact-mode controls[/]"));
        content.Add(table);

        return new Panel(new Rows(content))
        {
            Header = new PanelHeader($"Help · Azure Functions CLI {LogText.EscapeMarkup(_runInfo.CliVersion)}"),
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
