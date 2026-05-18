// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Builds the compact dashboard function search overlay.
/// </summary>
internal sealed class CompactFunctionSearchBuilder(ITheme theme, FunctionPalette palette)
{
    public const int ChromeLines = 5;

    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    private readonly FunctionPalette _palette = palette ?? throw new ArgumentNullException(nameof(palette));

    private string MutedTag => field ??= _theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= _theme.Emphasis.ToMarkup();

    public IRenderable Build(string query, FunctionInfo[] matches, int visibleRows, int rowOffset, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(matches);

        IRenderable results = matches.Length == 0
            ? new Markup($"[{MutedTag}]  No functions match \"{Markup.Escape(query)}\"[/]")
            : BuildResults(matches, visibleRows, rowOffset, selectedIndex);

        string displayQuery = query.Length == 0 ? "type to search" : query;
        return new Panel(new Rows(
            new Markup($"[{MutedTag}]Search:[/] [{EmphasisTag}]{Markup.Escape(displayQuery)}[/]"),
            results,
            new Markup(string.Empty),
            new Markup($"[{MutedTag}]Type to filter · Up/Down select · Enter apply · Esc cancel[/]")))
        {
            Header = new PanelHeader("Search functions"),
            Border = BoxBorder.Rounded,
            BorderStyle = _theme.Muted,
            Expand = true,
        };
    }

    public FunctionInfo[] GetMatches(FunctionInfo[] functions, string query)
    {
        ArgumentNullException.ThrowIfNull(functions);

        string trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return functions;
        }

        return [.. functions.Where(fn =>
            IsFuzzyMatch(fn.Name, trimmed)
            || IsFuzzyMatch(fn.TriggerType, trimmed)
            || IsFuzzyMatch(fn.Route, trimmed))];
    }

    public int GetVisibleRows(int matchCount, int viewportHeight)
    {
        int maxRows = Math.Max(3, viewportHeight - 14);
        return Math.Min(matchCount, maxRows);
    }

    private IRenderable BuildResults(FunctionInfo[] matches, int visibleRows, int rowOffset, int selectedIndex)
    {
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(2).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadRight(0));

        for (int i = 0; i < visibleRows; i++)
        {
            int index = rowOffset + i;
            if ((uint)index >= (uint)matches.Length)
            {
                break;
            }

            FunctionInfo function = matches[index];
            string marker = index == selectedIndex
                ? $"[{EmphasisTag}]>[/]"
                : " ";
            string color = _palette.GetColorFor(function.Name);
            string route = string.IsNullOrEmpty(function.Route) ? FormatTrigger(function.TriggerType) : function.Route;

            table.AddRow(
                new Markup(marker),
                new Markup($"[{color}]{Markup.Escape(function.Name)}[/]"),
                new Markup($"[{MutedTag}]{Markup.Escape(route)}[/]"));
        }

        return table;
    }

    private static string FormatTrigger(string trigger) => trigger switch
    {
        "http" => "HTTP",
        "queue" => "Queue",
        "timer" => "Timer",
        "blob" => "Blob",
        "eventhub" => "EventHub",
        "servicebus" => "ServiceBus",
        _ => trigger,
    };

    private static bool IsFuzzyMatch(string? value, string query)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int queryIndex = 0;
        foreach (char valueChar in value)
        {
            if (char.ToUpperInvariant(valueChar) == char.ToUpperInvariant(query[queryIndex]))
            {
                queryIndex++;
                if (queryIndex == query.Length)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
