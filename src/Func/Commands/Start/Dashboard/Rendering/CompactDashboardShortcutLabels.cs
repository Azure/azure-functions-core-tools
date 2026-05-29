// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Provides OS-specific shortcut labels for the compact dashboard.
/// </summary>
internal sealed class CompactDashboardShortcutLabels(IPlatform platform)
{
    private const string DefaultLogsNavigationControlLabel = "↑/↓, PgUp/PgDn logs";
    private const string MacOSLogsNavigationControlLabel = "↑/↓, Fn+↑/↓ logs";
    private const string DefaultPageNavigationHelpKey = "PgUp/PgDn";
    private const string MacOSPageNavigationHelpKey = "Fn+↑/↓";

    private readonly IPlatform _platform = platform ?? throw new ArgumentNullException(nameof(platform));

    public string LogsNavigationControlLabel => _platform.IsMacOS
        ? MacOSLogsNavigationControlLabel
        : DefaultLogsNavigationControlLabel;

    public string PageNavigationHelpKey => _platform.IsMacOS
        ? MacOSPageNavigationHelpKey
        : DefaultPageNavigationHelpKey;
}
