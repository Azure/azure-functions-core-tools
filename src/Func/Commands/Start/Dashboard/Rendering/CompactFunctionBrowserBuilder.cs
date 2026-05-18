// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Builds the compact dashboard function browser overlay.
/// </summary>
internal sealed class CompactFunctionBrowserBuilder(ITheme theme, FunctionPalette palette)
{
    public const int ChromeLines = 4;

    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    private readonly FunctionPalette _palette = palette ?? throw new ArgumentNullException(nameof(palette));

    private string MutedTag => field ??= _theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= _theme.Emphasis.ToMarkup();
    private string SuccessTag => field ??= _theme.Success.ToMarkup();
    private string ErrorTag => field ??= _theme.Error.ToMarkup();
    private string ActiveTag => field ??= _theme.Active.ToMarkup();

    public IRenderable Build(FunctionInfo[] functions, int totalRows, int visibleRows, int rowOffset, int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(functions);

        IRenderable content = functions.Length == 0
            ? new Markup($"[{MutedTag}]No functions loaded yet…[/]")
            : BuildGrid(functions, totalRows, visibleRows, rowOffset, selectedIndex);

        Markup footer = new($"[{MutedTag}]Up/Down navigate · Enter filter · / search · f next · a all · t/Esc close · q/Ctrl+C[/]");
        return new Panel(new Rows(content, new Markup(string.Empty), footer))
        {
            Header = new PanelHeader(string.Create(CultureInfo.InvariantCulture, $"Functions ({functions.Length})")),
            Border = BoxBorder.Rounded,
            BorderStyle = _theme.Muted,
            Expand = true,
        };
    }

    public int GetTotalRows(int functionCount)
        => Math.Max(1, (functionCount + 1) / 2);

    public int GetRow(int index, int totalRows)
        => totalRows <= 0 ? 0 : index % totalRows;

    public int GetVisibleRows(int functionCount, int viewportHeight)
    {
        int totalRows = GetTotalRows(functionCount);

        // Reserve room for the log rule, minimum log tail, safety padding, and browser panel chrome.
        int maxRows = Math.Max(4, viewportHeight - 10);
        return Math.Min(totalRows, maxRows);
    }

    private IRenderable BuildGrid(
        FunctionInfo[] functions,
        int totalRows,
        int visibleRows,
        int rowOffset,
        int selectedIndex)
    {
        Table table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(4).NoWrap())
            .AddColumn(new TableColumn(string.Empty).PadLeft(1).PadRight(0).NoWrap());

        for (int row = 0; row < visibleRows; row++)
        {
            int leftIndex = rowOffset + row;
            int rightIndex = leftIndex + totalRows;

            table.AddRow(
                new Markup(FormatCell(functions, leftIndex, selectedIndex)),
                new Markup(FormatCell(functions, rightIndex, selectedIndex)));
        }

        return table;
    }

    private string FormatCell(FunctionInfo[] functions, int index, int selectedIndex)
    {
        if ((uint)index >= (uint)functions.Length)
        {
            return string.Empty;
        }

        FunctionInfo function = functions[index];
        string marker = index == selectedIndex
            ? $"[{EmphasisTag}]>[/]"
            : " ";
        string status = FormatStatus(function);
        string color = _palette.GetColorFor(function.Name);
        string activeCount = function.Status == FunctionStatus.Active && function.ActiveInvocations > 1
            ? string.Create(CultureInfo.InvariantCulture, $" ({function.ActiveInvocations})")
            : string.Empty;

        return $"{marker} {status} [{color}]{Markup.Escape(function.Name)}[/]{activeCount}";
    }

    private string FormatStatus(FunctionInfo function) => function.Status switch
    {
        FunctionStatus.Active => $"[{ActiveTag}]◉[/]",
        FunctionStatus.Error => $"[{ErrorTag}]✗[/]",
        _ => $"[{SuccessTag}]●[/]",
    };
}
