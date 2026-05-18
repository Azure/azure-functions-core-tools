// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console.Theme;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Builds the compact dashboard footer and control hints.
/// </summary>
internal sealed class CompactFooterBuilder(ITheme theme, DashboardRunInfo runInfo)
{
    private const string HelpCloseControlLabel = "?/Esc close";
    private const string LogsNavigationControlLabel = "↑/↓, PgUp/PgDn logs";
    private const string FunctionBrowserControlLabel = "t functions";
    private const string QuitControlLabel = "q/Ctrl+C quit";
    private const string FunctionFilterToggleControlLabel = "f next";
    private const string HelpControlLabel = "? help";

    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    private readonly DashboardRunInfo _runInfo = runInfo ?? throw new ArgumentNullException(nameof(runInfo));

    private string MutedTag => field ??= _theme.Muted.ToMarkup();

    public IRenderable Build(
        DashboardSnapshot snapshot,
        string? activeFunctionFilter,
        bool errorsOnly,
        LogLevel minimumLogLevel,
        int logScrollOffset,
        bool helpOpen,
        bool functionSearchOpen,
        bool functionBrowserOpen)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string filter = activeFunctionFilter is not null
            ? $" · Filter {activeFunctionFilter}"
            : string.Empty;

        string errors = errorsOnly
            ? " · Errors only"
            : string.Empty;

        string level = $" · L:{FormatMinimumLogLevel(minimumLogLevel)}";
        string logScroll = logScrollOffset > 0
            ? $" · Scrollback {logScrollOffset}"
            : string.Empty;

        string controls = (helpOpen, functionSearchOpen, functionBrowserOpen, activeFunctionFilter is not null) switch
        {
            (true, _, _, _) => $"{HelpCloseControlLabel} · {QuitControlLabel}",
            (_, true, _, _) => "type query · ↑/↓ select · Enter filter · Esc close",
            (_, _, true, _) => $"↑/↓ navigate · Enter filter · {FunctionFilterToggleControlLabel} · {FunctionBrowserControlLabel}",
            (_, _, _, true) => $"{LogsNavigationControlLabel} · {FunctionFilterToggleControlLabel} · a all · {HelpControlLabel} · {QuitControlLabel}",
            _ => $"{LogsNavigationControlLabel} · {FunctionBrowserControlLabel} · {HelpControlLabel} · {QuitControlLabel}",
        };

        string line = string.Create(
            CultureInfo.InvariantCulture,
            $"{_runInfo.CliVersion} · {snapshot.Functions.Count} functions · {snapshot.TotalInvocations} invocations · {snapshot.ErrorCount} error{(snapshot.ErrorCount == 1 ? string.Empty : "s")}{filter}{errors}{level}{logScroll} │ {controls}");

        return new Markup($"[{MutedTag}]{Markup.Escape(line)}[/]");
    }

    private static string FormatMinimumLogLevel(LogLevel minimumLogLevel) => minimumLogLevel switch
    {
        LogLevel.Warning => "warn",
        LogLevel.Error or LogLevel.Critical => "error",
        _ => "info",
    };
}
