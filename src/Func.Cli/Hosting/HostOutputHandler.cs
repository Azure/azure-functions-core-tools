// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.RegularExpressions;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Hosting;

/// <summary>
/// Intercepts host stdout to parse function route mappings,
/// display clickable URLs, filter verbose output, and suppress noise.
/// </summary>
public partial class HostOutputHandler
{
    private readonly IInteractionService _interaction;
    private readonly int _port;
    private readonly bool _verbose;
    private readonly List<FunctionRoute> _routes = [];
    private readonly List<string> _nonHttpFunctions = [];
    private bool _routesDisplayed;
    private bool _suppressingStackTrace;
    private bool _shuttingDown;

    // Track multi-line JSON blocks (OptionsLoggingService dumps)
    private bool _inJsonBlock;
    private int _braceDepth;

    // Track whether continuation lines (6-space indented) should be kept.
    private bool _keepContinuationLines;

    public HostOutputHandler(IInteractionService interaction, int port, bool verbose)
    {
        _interaction = interaction;
        _port = port;
        _verbose = verbose;
    }

    /// <summary>
    /// Processes a line of host output. Returns true if the line should be
    /// written to the terminal, false if it should be suppressed.
    /// </summary>
    public bool ProcessLine(string line)
    {
        var stripped = StripAnsi(line);

        // Detect shutdown and suppress all noise in non-verbose mode
        if (stripped.Contains("Application is shutting down"))
        {
            _shuttingDown = true;
            if (!_verbose)
            {
                _interaction.WriteBlankLine();
                _interaction.WriteMarkupLine("[grey]Host shutting down...[/]");
                return false;
            }
        }

        if (_shuttingDown && !_verbose)
        {
            return false;
        }

        // Suppress the noisy shared memory warning on macOS
        if (stripped.Contains("SharedMemoryDataTransfer") ||
            stripped.Contains("/dev/shm") ||
            stripped.Contains("Cannot create directory for shared memory") ||
            stripped.Contains("MemoryMappedFileAccessor"))
        {
            _suppressingStackTrace = true;
            return false;
        }

        if (_suppressingStackTrace)
        {
            var trimmed = stripped.TrimStart();
            if (trimmed.StartsWith("at ") || trimmed.StartsWith("---") || trimmed.StartsWith("---> "))
            {
                return false;
            }

            _suppressingStackTrace = false;
        }

        // Parse route mappings
        var routeMatch = RoutePattern().Match(stripped);
        if (routeMatch.Success)
        {
            _routes.Add(new FunctionRoute(
                Name: routeMatch.Groups[3].Value,
                Route: routeMatch.Groups[1].Value,
                Methods: routeMatch.Groups[2].Value));

            return false;
        }

        // Parse timer trigger schedules (e.g., "The next 5 occurrences of the 'TimerTrigger1' schedule")
        var timerMatch = TimerPattern().Match(stripped);
        if (timerMatch.Success)
        {
            var name = timerMatch.Groups[1].Value;
            var cron = timerMatch.Groups[2].Value;
            if (!_nonHttpFunctions.Contains(name))
            {
                _nonHttpFunctions.Add(name);
            }
        }

        if (stripped.Contains("Initializing function HTTP routes"))
        {
            return false;
        }

        // When host reports it's started, display the collected function URLs
        if (!_routesDisplayed && stripped.Contains("Host started"))
        {
            DisplayFunctions();
            _routesDisplayed = true;
        }

        // Suppress the host's own Ctrl+C message (we show our own at startup)
        if (stripped.Contains("Application started. Press Ctrl+C"))
        {
            return false;
        }

        if (_verbose)
        {
            return true;
        }

        // --- Non-verbose filtering below ---

        if (_inJsonBlock)
        {
            foreach (var ch in stripped)
            {
                if (ch == '{') _braceDepth++;
                else if (ch == '}') _braceDepth--;
            }
            if (_braceDepth <= 0)
            {
                _inJsonBlock = false;
            }
            return false;
        }

        if (stripped.Contains("OptionsLoggingService"))
        {
            _inJsonBlock = true;
            _braceDepth = 0;
            return false;
        }

        if (IsConfigBlockHeader(stripped))
        {
            _inJsonBlock = true;
            _braceDepth = 0;
            return false;
        }

        if (IsNoisyLine(stripped))
        {
            return false;
        }

        return true;
    }

    private bool IsNoisyLine(string line)
    {
        if (line.StartsWith("      "))
        {
            if (_keepContinuationLines)
            {
                return false;
            }

            return true;
        }

        if (line.StartsWith("warn:") || line.StartsWith("fail:"))
        {
            _keepContinuationLines = true;
            return false;
        }

        if (line.StartsWith("info:"))
        {
            if (line.StartsWith("info: Function.") ||
                line.StartsWith("info: Host.Triggers."))
            {
                _keepContinuationLines = true;
                return false;
            }

            _keepContinuationLines = false;
            return true;
        }

        if (line.StartsWith("Hosting environment:") ||
            line.StartsWith("Content root path:") ||
            line.StartsWith("Now listening on:"))
        {
            return true;
        }

        return false;
    }

    private static bool IsConfigBlockHeader(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 0 &&
               char.IsUpper(trimmed[0]) &&
               trimmed.EndsWith("Options") &&
               !trimmed.Contains(' ');
    }

    private void DisplayFunctions()
    {
        if (_routes.Count == 0 && _nonHttpFunctions.Count == 0)
        {
            return;
        }

        _interaction.WriteBlankLine();
        _interaction.WriteMarkupLine("[bold yellow]Functions:[/]");
        _interaction.WriteBlankLine();

        foreach (var route in _routes)
        {
            var methods = route.Methods.ToUpperInvariant().Replace(" ", "");
            var url = $"http://localhost:{_port}/{route.Route}";
            _interaction.WriteMarkupLine(
                $"        [white]{Esc(route.Name)}:[/] [grey][[{Esc(methods)}]][/] [green link={Esc(url)}]{Esc(url)}[/]");
        }

        foreach (var name in _nonHttpFunctions)
        {
            _interaction.WriteMarkupLine(
                $"        [white]{Esc(name)}:[/] [grey][[Timer]][/]");
        }

        _interaction.WriteBlankLine();
    }

    private static string Esc(string text) => Spectre.Console.Markup.Escape(text);

    private static string StripAnsi(string text) => AnsiPattern().Replace(text, "");

    private record FunctionRoute(string Name, string Route, string Methods);

    [GeneratedRegex(@"Mapped function route '(.+?)' \[(.+?)\] to '(.+?)'")]
    private static partial Regex RoutePattern();

    [GeneratedRegex(@"occurrences of the '(.+?)' schedule \(Cron: '(.+?)'\)")]
    private static partial Regex TimerPattern();

    [GeneratedRegex(@"\x1B\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiPattern();
}
