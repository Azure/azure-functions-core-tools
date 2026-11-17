// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

internal sealed record CompactHelpPage(IRenderable Content, int RowOffset, int MaximumRowOffset, int VisibleRows);

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

    public CompactHelpPage Build(int width, int maxHeight, int rowOffset = 0, string? status = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 3);

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
        Add("↑/↓", "Scroll logs or help; move overlay selection.", "Scroll / select");
        Add("Mouse wheel", "Scroll the log pane.", "Scroll logs");
        Add("Drag", "Select log text; Ctrl+C copies.", "Select text");
        Add(_shortcutLabels.PageNavigationHelpKey, "Page through logs, functions, or help.", "Page");
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

        int innerWidth = Math.Max(1, width - 4);
        RenderOptions options = LogText.CreateRenderOptions(innerWidth, int.MaxValue) with { Height = null };
        IRenderable body = new Rows(content);
        List<SegmentLine> lines = Segment.SplitLines(body.Render(options, innerWidth));
        int visibleRows = Math.Min(lines.Count, maxHeight - 2);
        int maximumOffset = Math.Max(0, lines.Count - visibleRows);
        rowOffset = Math.Clamp(rowOffset, 0, maximumOffset);
        string position = maximumOffset == 0 ? string.Empty
            : FormattableString.Invariant($" · {rowOffset + 1}-{rowOffset + visibleRows}/{lines.Count}");
        Panel panel = new(new HelpRows(lines.GetRange(rowOffset, visibleRows)))
        {
            Header = new PanelHeader($"Help{position} · Azure Functions CLI {LogText.EscapeMarkup(_runInfo.CliVersion)}"),
            Border = BoxBorder.Rounded,
            BorderStyle = _theme.Muted,
            Expand = true,
        };
        return new CompactHelpPage(panel, rowOffset, maximumOffset, visibleRows);
    }

    private void AddHelpRow(Table table, string key, string description)
    {
        table.AddRow(
            new Markup($"[{CommandTag}]{Markup.Escape(key)}[/]"),
            new Markup($"[{MutedTag}]{Markup.Escape(description)}[/]"));
    }

    private sealed class HelpRows(IReadOnlyList<SegmentLine> lines) : Renderable
    {
        protected override Measurement Measure(RenderOptions options, int maxWidth) => new(0, maxWidth);

        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            for (int row = 0; row < lines.Count; row++)
            {
                if (row > 0)
                {
                    yield return Segment.LineBreak;
                }

                foreach (Segment segment in lines[row])
                {
                    yield return segment;
                }
            }
        }
    }
}
