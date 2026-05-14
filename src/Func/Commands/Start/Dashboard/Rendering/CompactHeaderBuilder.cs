// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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

    public IRenderable BuildBanner(string? hostVersion, string? listenUri)
    {
        string host = string.IsNullOrWhiteSpace(hostVersion) ? "—" : hostVersion;
        string listen = string.IsNullOrWhiteSpace(listenUri) ? "—" : listenUri;

        Table bannerTable = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .Expand()
            .AddColumn(new TableColumn(string.Empty).NoWrap().PadLeft(0).PadRight(0))
            .AddColumn(new TableColumn(string.Empty).RightAligned().NoWrap().PadLeft(0).PadRight(0));

        bannerTable.AddRow(
            new Markup($"[{WarningTag}]:high_voltage:[/] [{TitleTag}]Azure Functions CLI[/]  " +
            $"[{MutedTag}]Host:[/] [{EmphasisTag}]{Markup.Escape(host)}[/][{MutedTag}] · " +
            $"Profile:[/] [{EmphasisTag}]{Markup.Escape(_runInfo.ProfileName)}[/][{MutedTag}] · " +
            $"Stack:[/] [{EmphasisTag}]{Markup.Escape(_runInfo.StackName)}[/]"),
            new Markup($"[{HyperlinkTag}]{Markup.Escape(listen)}[/]"));

        return new Panel(bannerTable)
            .Border(BoxBorder.Rounded)
            .BorderStyle(_theme.Muted)
            .Expand();
    }
}
