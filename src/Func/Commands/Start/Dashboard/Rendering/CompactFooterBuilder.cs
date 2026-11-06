// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Theme;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

internal enum CompactFooterMode
{
    Logs,
    Selection,
    ExpiredSelection,
    Search,
    Functions,
    Help,
    MouseReleased,
    Stopping,
}

/// <summary>
/// Fits complete, contextual shortcuts into a single row with a reserved stop action.
/// </summary>
internal sealed class CompactFooterBuilder(ITheme theme, CompactDashboardShortcutLabels shortcutLabels)
{
    private const string Separator = "  ";
    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    private readonly CompactDashboardShortcutLabels _shortcutLabels = shortcutLabels ?? throw new ArgumentNullException(nameof(shortcutLabels));

    public IRenderable Build(int width, CompactFooterMode mode, bool following = true, bool filtered = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        string stop = mode switch
        {
            CompactFooterMode.Stopping => "Ctrl+C force exit",
            CompactFooterMode.Search => "Ctrl+C stop",
            _ => "q quit",
        };
        if (LogText.Measure(stop) > width)
        {
            stop = mode is CompactFooterMode.Stopping or CompactFooterMode.Search ? "Ctrl+C" : "q";
        }

        string right = LogText.Measure(stop) <= width ? stop : string.Empty;
        if (mode is not (CompactFooterMode.Search or CompactFooterMode.Help or CompactFooterMode.Stopping)
            && LogText.Measure("? help" + Separator + right) <= width)
        {
            right = "? help" + Separator + right;
        }

        List<string> actions = mode switch
        {
            CompactFooterMode.Selection => ["Ctrl+C copy", "Esc clear", "m native copy"],
            CompactFooterMode.ExpiredSelection => ["Esc clear", "m native copy"],
            CompactFooterMode.Search => ["Esc close", "Enter filter", "↑/↓ select", "type query"],
            CompactFooterMode.Functions => ["Esc close", "Enter filter", "↑/↓ navigate", "f next", "/ search"],
            CompactFooterMode.Help => ["?/Esc close", "Ctrl+C stop"],
            CompactFooterMode.MouseReleased => ["m resume", "Ctrl+C stop"],
            CompactFooterMode.Stopping => [],
            _ => ["↑/↓ scroll", "t functions", "Ctrl+C stop", "/ search", $"{_shortcutLabels.PageNavigationHelpKey} page", "m native copy"],
        };
        if (mode == CompactFooterMode.Logs)
        {
            if (filtered)
            {
                actions.Insert(0, "a all");
            }
            if (!following)
            {
                actions.Insert(0, "End live");
            }
        }

        int available = width - LogText.Measure(right) - (right.Length == 0 ? 0 : Separator.Length);
        string left = string.Empty;
        foreach (string action in actions)
        {
            string candidate = left.Length == 0 ? action : left + Separator + action;
            if (LogText.Measure(candidate) <= available)
            {
                left = candidate;
            }
        }

        int padding = width - LogText.Measure(left) - LogText.Measure(right);
        return new Text(left + new string(' ', padding) + right, _theme.Muted);
    }
}
