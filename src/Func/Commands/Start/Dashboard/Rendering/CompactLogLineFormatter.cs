// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Formats host and dashboard events into compact dashboard log rows.
/// </summary>
internal sealed class CompactLogLineFormatter(ITheme theme, FunctionPalette palette)
{
    private const int SourceColumnWidth = 18;

    private readonly ITheme _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    private readonly FunctionPalette _palette = palette ?? throw new ArgumentNullException(nameof(palette));

    private string MutedTag => field ??= _theme.Muted.ToMarkup();
    private string SuccessTag => field ??= _theme.Success.ToMarkup();
    private string ErrorTag => field ??= _theme.Error.ToMarkup();
    private string WarningTag => field ??= _theme.Warning.ToMarkup();

    public CompactLogLine? Format(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, string? listenUri)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(events);

        if (ShouldSuppress(entry, events))
        {
            return null;
        }

        bool isError = IsErrorLogLine(entry, events);
        string? functionName = GetFunctionName(entry, events);
        LogLevel effectiveLogLevel = GetEffectiveLogLevel(entry, isError);
        return FormatLine(entry, events, listenUri, functionName, isError, effectiveLogLevel);
    }

    private CompactLogLine FormatLine(
        HostLogEntry entry,
        IReadOnlyList<DashboardEvent> events,
        string? listenUri,
        string? functionName,
        bool isError,
        LogLevel effectiveLogLevel)
    {
        string ts = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        foreach (DashboardEvent ev in events)
        {
            switch (ev)
            {
                case HostStateChangedEvent hs:
                    return CreateSingleLine(
                        string.Create(CultureInfo.InvariantCulture,
                            $" [{MutedTag}]{ts}[/]  [{MutedTag}][[host]][/]             {Markup.Escape(DescribeHostState(hs))}"),
                        functionName,
                        isError,
                        effectiveLogLevel);

                case FunctionDiscoveredEvent fd:
                {
                    string color = _palette.GetColorFor(fd.Function.Name);
                    string routeMarkup = HttpRouteFormatter.FormatRouteMarkup(fd.Function, listenUri, _theme);
                    return CreateSingleLine(
                        string.Create(CultureInfo.InvariantCulture,
                            $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(fd.Function.Name),-18}[/]  loaded  [{MutedTag}]{Markup.Escape(fd.Function.TriggerType)} {routeMarkup}[/]"),
                        functionName,
                        isError,
                        effectiveLogLevel);
                }

                case InvocationStartedEvent inv:
                {
                    string color = _palette.GetColorFor(inv.Function);
                    string detail = string.Empty;
                    if (inv.Attributes.TryGetValue(HostLogAttributeKeys.HttpMethod, out object? method) &&
                        inv.Attributes.TryGetValue(HostLogAttributeKeys.HttpTarget, out object? target))
                    {
                        detail = $"{method} {target}";
                    }

                    return CreateSingleLine(
                        string.Create(CultureInfo.InvariantCulture,
                            $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(inv.Function),-18}[/]  [{SuccessTag}]→[/]  {Markup.Escape(detail)}"),
                        functionName,
                        isError,
                        effectiveLogLevel);
                }

                case InvocationCompletedEvent inv:
                {
                    string color = _palette.GetColorFor(inv.Function);
                    bool failed = string.Equals(inv.Result, "failed", StringComparison.OrdinalIgnoreCase);
                    string arrow = failed ? $"[{ErrorTag}]✗[/]" : $"[{SuccessTag}]←[/]";
                    string suffix = failed
                        ? $"[{ErrorTag}]{Markup.Escape(inv.ErrorSummary ?? string.Empty)}[/]"
                        : $"[{MutedTag}]{(inv.DurationMs.HasValue ? ((long)inv.DurationMs.Value).ToString(CultureInfo.InvariantCulture) + "ms" : string.Empty)}[/]";

                    return CreateSingleLine(
                        string.Create(CultureInfo.InvariantCulture,
                            $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(inv.Function),-18}[/]  {arrow}  {suffix}"),
                        functionName,
                        isError,
                        effectiveLogLevel);
                }
            }
        }

        string source = functionName ?? entry.Category;
        int sourceWidth = Math.Max(SourceColumnWidth, source.Length);
        string nameMarkup = functionName is not null
            ? $"[{_palette.GetColorFor(functionName)}]{Markup.Escape(functionName),-SourceColumnWidth}[/]"
            : $"[{MutedTag}]{Markup.Escape(entry.Category),-SourceColumnWidth}[/]";

        string levelMarkup = entry.Level switch
        {
            LogLevel.Error or LogLevel.Critical => $"[{ErrorTag}]✗[/]",
            LogLevel.Warning => $"[{WarningTag}]![/]",
            _ => $"[{MutedTag}]·[/]",
        };

        string prefix = string.Create(CultureInfo.InvariantCulture, $" [{MutedTag}]{ts}[/]  {nameMarkup}  {levelMarkup}  ");
        int prefixWidth = 1 + ts.Length + 2 + sourceWidth + 2 + 1 + 2;
        return CreateWrappedLine(prefix, prefixWidth, FormatMessage(entry), functionName, isError, effectiveLogLevel);
    }

    private static CompactLogLine CreateSingleLine(string markup, string? functionName, bool isError, LogLevel level)
        => new(new Markup(markup), functionName, isError, level);

    private static string FormatMessage(HostLogEntry entry)
    {
        string? exceptionSummary = entry.ExceptionDetails?.FormatSummary();
        if (string.IsNullOrEmpty(exceptionSummary))
        {
            return entry.Message;
        }

        return string.IsNullOrWhiteSpace(entry.Message)
            ? exceptionSummary
            : entry.Message + Environment.NewLine + exceptionSummary;
    }

    private static CompactLogLine CreateWrappedLine(
        string prefixMarkup,
        int prefixWidth,
        string message,
        string? functionName,
        bool isError,
        LogLevel level)
    {
        string continuationPrefix = new(' ', prefixWidth);
        Markup first = new(prefixMarkup + Markup.Escape(message));
        return new CompactLogLine(
            first,
            width => BuildWrappedRows(prefixMarkup, prefixWidth, continuationPrefix, message, width),
            functionName,
            isError,
            level);
    }

    private static IReadOnlyList<IRenderable> BuildWrappedRows(
        string firstPrefixMarkup,
        int prefixWidth,
        string continuationPrefix,
        string message,
        int viewportWidth)
    {
        int messageWidth = Math.Max(1, viewportWidth - prefixWidth);
        List<string> segments = WrapMessage(message, messageWidth);
        if (segments.Count == 0)
        {
            segments.Add(string.Empty);
        }

        var rows = new List<IRenderable>(segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            string prefix = i == 0 ? firstPrefixMarkup : continuationPrefix;
            rows.Add(new Markup(prefix + Markup.Escape(segments[i])));
        }

        return rows;
    }

    private static List<string> WrapMessage(string message, int width)
    {
        var rows = new List<string>();
        foreach (string line in NormalizeLineEndings(message).Split('\n'))
        {
            if (line.Length == 0)
            {
                rows.Add(string.Empty);
                continue;
            }

            for (int start = 0; start < line.Length; start += width)
            {
                int length = Math.Min(width, line.Length - start);
                rows.Add(line.Substring(start, length));
            }
        }

        return rows;
    }

    private static string NormalizeLineEndings(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool ShouldSuppress(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        if (events.Count > 0)
        {
            return false;
        }

        if (string.Equals(entry.Category, "Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService", StringComparison.Ordinal))
        {
            return true;
        }

        return entry.ExceptionDetails is null
            && (string.IsNullOrWhiteSpace(entry.Message)
                || IsFunctionsInvocationEnvelope(entry.Message));
    }

    private static bool IsFunctionsInvocationEnvelope(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.StartsWith("Executing '", StringComparison.Ordinal)
            || message.StartsWith("Executed '", StringComparison.Ordinal);
    }

    private static string? GetFunctionName(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        foreach (DashboardEvent ev in events)
        {
            switch (ev)
            {
                case FunctionDiscoveredEvent fd:
                    return fd.Function.Name;
                case InvocationStartedEvent started:
                    return started.Function;
                case InvocationCompletedEvent completed:
                    return completed.Function;
            }
        }

        return entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
    }

    private static string DescribeHostState(HostStateChangedEvent hs) => (hs.From, hs.To) switch
    {
        (_, HostLifecycleState.Ready) when hs.DurationMs is { } d => $"Host ready ({(d / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}s)",
        (_, HostLifecycleState.Ready) => "Host ready",
        (_, HostLifecycleState.Recycling) when !string.IsNullOrEmpty(hs.Trigger) => $"Recycling (file changed: {hs.Trigger})",
        (_, HostLifecycleState.Recycling) => "Recycling",
        (_, HostLifecycleState.Starting) => "Host starting",
        (_, HostLifecycleState.Stopped) => "Host stopped",
        _ => $"Host {hs.To.ToString().ToLowerInvariant()}",
    };

    private static bool IsErrorLogLine(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        if (entry.Level is LogLevel.Error or LogLevel.Critical)
        {
            return true;
        }

        return events.Any(static ev =>
            ev is InvocationCompletedEvent { Result: var result }
            && string.Equals(result, "failed", StringComparison.OrdinalIgnoreCase));
    }

    private static LogLevel GetEffectiveLogLevel(HostLogEntry entry, bool isError)
        => isError && entry.Level < LogLevel.Error
            ? LogLevel.Error
            : entry.Level;
}
