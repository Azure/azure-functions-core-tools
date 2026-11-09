// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Builds the compact renderer's top banner.
/// </summary>
internal sealed class CompactHeaderBuilder(ITheme theme, DashboardRunInfo runInfo)
{
    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    private readonly DashboardRunInfo _runInfo = runInfo ?? throw new ArgumentNullException(nameof(runInfo));

    private string MutedTag => field ??= _theme.Muted.ToMarkup();
    private string EmphasisTag => field ??= _theme.Emphasis.ToMarkup();
    private string TitleTag => field ??= _theme.Title.ToMarkup();
    private string WarningTag => field ??= _theme.Warning.ToMarkup();
    private string HyperlinkTag => field ??= _theme.Hyperlink.ToMarkup();

    public IRenderable BuildBanner(DashboardSnapshot snapshot, int width)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        string host = string.IsNullOrWhiteSpace(snapshot.HostVersion) ? "—" : snapshot.HostVersion;
        string listen = string.IsNullOrWhiteSpace(snapshot.ListenUri) ? "—" : snapshot.ListenUri;

        Table bannerTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).NoWrap().PadLeft(0).PadRight(0))
            .AddColumn(new TableColumn(string.Empty).RightAligned().NoWrap().PadLeft(0).PadRight(0));

        bannerTable.AddRow(
            new Markup($"[{WarningTag}]:high_voltage:[/] [{TitleTag}]Azure Functions CLI[/]  " +
            $"[{MutedTag}]Host:[/] [{EmphasisTag}]{LogText.EscapeMarkup(host)}[/][{MutedTag}] · " +
            $"Profile:[/] [{EmphasisTag}]{LogText.EscapeMarkup(_runInfo.ProfileName)}[/][{MutedTag}] · " +
            $"Stack:[/] [{EmphasisTag}]{LogText.EscapeMarkup(_runInfo.StackName)}[/]"),
            new Markup($"[{HyperlinkTag}]{LogText.EscapeMarkup(listen)}[/]"));

        string totals = string.Create(CultureInfo.InvariantCulture,
            $"{snapshot.Functions.Count} functions · {snapshot.TotalInvocations} invocations · {snapshot.ErrorCount} error{(snapshot.ErrorCount == 1 ? string.Empty : "s")}");
        if (LogText.Measure(totals) > width - 6)
        {
            totals = string.Create(CultureInfo.InvariantCulture,
                $"{snapshot.Functions.Count} fn · {snapshot.TotalInvocations} inv · {snapshot.ErrorCount} err");
        }

        return new Panel(bannerTable)
            .Header(LogText.EscapeMarkup(LogText.Clip(totals, width - 6)))
            .Border(BoxBorder.Rounded)
            .BorderStyle(_theme.Muted)
            .Expand();
    }

    public IRenderable BuildLogDivider(int width, IReadOnlyList<string> state, string label = "Logs")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        int available = Math.Max(0, width - 6);
        string title = LogText.Clip(label, available);
        foreach (string item in state)
        {
            string text = LogText.Sanitize(item).Replace('\n', ' ');
            int remaining = available - LogText.Measure(title) - 3;
            if (remaining < 4)
            {
                break;
            }

            title += " · " + LogText.Clip(text, remaining);
            if (LogText.Measure(text) > remaining)
            {
                break;
            }
        }

        return width < 10
            ? new Text(LogText.Clip(label, width), _theme.Muted)
            : new Rule(LogText.EscapeMarkup(title)).LeftJustified().RuleStyle(_theme.Muted);
    }
}
