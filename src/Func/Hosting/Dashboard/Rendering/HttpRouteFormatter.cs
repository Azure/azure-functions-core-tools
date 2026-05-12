// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Spectre.Console;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Formats a function's "Route / Source" column value as Spectre markup.
/// For HTTP-triggered functions with a known host listen URI, the route
/// renders as a clickable OSC 8 hyperlink showing the full endpoint URL —
/// matching the legacy <c>func start</c> behavior where developers click
/// the printed URL to invoke their function. Non-HTTP triggers and HTTP
/// functions where the listen URI is not yet known render as plain text.
/// </summary>
/// <remarks>
/// OSC 8 hyperlinks are honored by modern terminals (Windows Terminal,
/// Windows PowerShell on Windows 11, VS Code, iTerm2, GNOME Terminal,
/// Konsole, ...). Terminals that don't recognize the sequence simply show
/// the display text and ignore the link target — graceful degradation, no
/// stray escape characters leaked into the output.
/// </remarks>
internal static class HttpRouteFormatter
{
    /// <summary>
    /// Returns Spectre markup describing the function's route. The returned
    /// string is safe to embed directly into another markup template.
    /// </summary>
    public static string FormatRouteMarkup(FunctionInfo function, string? listenUri)
    {
        ArgumentNullException.ThrowIfNull(function);

        string methodsPrefix = function.HttpMethods.Count > 0
            ? string.Join(",", function.HttpMethods) + " "
            : string.Empty;

        bool isHttp = string.Equals(function.TriggerType, "http", StringComparison.OrdinalIgnoreCase);
        if (!isHttp || string.IsNullOrEmpty(listenUri) || string.IsNullOrEmpty(function.Route))
        {
            string fallback = !string.IsNullOrEmpty(function.Route) ? function.Route : "—";
            return Markup.Escape(methodsPrefix + fallback);
        }

        string url = CombineUrl(listenUri, function.Route);

        // [link=URL]DISPLAY[/] emits OSC 8 in supported terminals. We show
        // the full URL as the display text so it's obvious what gets opened
        // when clicked, and so the output remains useful when copy-pasted
        // into a terminal that doesn't honor the hyperlink sequence.
        return $"{Markup.Escape(methodsPrefix)}[link={Markup.Escape(url)}]{Markup.Escape(url)}[/]";
    }

    private static string CombineUrl(string baseUrl, string route)
    {
        string left = baseUrl.TrimEnd('/');
        string right = route.StartsWith('/') ? route : "/" + route;
        return left + right;
    }
}
