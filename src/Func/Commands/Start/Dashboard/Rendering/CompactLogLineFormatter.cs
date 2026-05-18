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
        return new CompactLogLine(
            FormatRenderable(entry, events, listenUri),
            GetFunctionName(entry, events),
            isError,
            GetEffectiveLogLevel(entry, isError));
    }

    private IRenderable FormatRenderable(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, string? listenUri)
    {
        string ts = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        foreach (DashboardEvent ev in events)
        {
            switch (ev)
            {
                case HostStateChangedEvent hs:
                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{MutedTag}][[host]][/]             {Markup.Escape(DescribeHostState(hs))}"));

                case FunctionDiscoveredEvent fd:
                {
                    string color = _palette.GetColorFor(fd.Function.Name);
                    string routeMarkup = HttpRouteFormatter.FormatRouteMarkup(fd.Function, listenUri, _theme);
                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(fd.Function.Name),-18}[/]  loaded  [{MutedTag}]{Markup.Escape(fd.Function.TriggerType)} {routeMarkup}[/]"));
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

                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(inv.Function),-18}[/]  [{SuccessTag}]→[/]  {Markup.Escape(detail)}"));
                }

                case InvocationCompletedEvent inv:
                {
                    string color = _palette.GetColorFor(inv.Function);
                    bool failed = string.Equals(inv.Result, "failed", StringComparison.OrdinalIgnoreCase);
                    string arrow = failed ? $"[{ErrorTag}]✗[/]" : $"[{SuccessTag}]←[/]";
                    string suffix = failed
                        ? $"[{ErrorTag}]{Markup.Escape(inv.ErrorType ?? string.Empty)}: {Markup.Escape(inv.ErrorMessage ?? string.Empty)}[/]"
                        : $"[{MutedTag}]{(inv.DurationMs.HasValue ? ((long)inv.DurationMs.Value).ToString(CultureInfo.InvariantCulture) + "ms" : string.Empty)}[/]";

                    return new Markup(string.Create(CultureInfo.InvariantCulture,
                        $" [{MutedTag}]{ts}[/]  [{color}]{Markup.Escape(inv.Function),-18}[/]  {arrow}  {suffix}"));
                }
            }
        }

        string? functionName = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
        string nameMarkup = functionName is not null
            ? $"[{_palette.GetColorFor(functionName)}]{Markup.Escape(functionName),-18}[/]"
            : $"[{MutedTag}]{Markup.Escape(entry.Category),-18}[/]";

        string levelMarkup = entry.Level switch
        {
            LogLevel.Error or LogLevel.Critical => $"[{ErrorTag}]✗[/]",
            LogLevel.Warning => $"[{WarningTag}]![/]",
            _ => $"[{MutedTag}]·[/]",
        };

        return new Markup(string.Create(CultureInfo.InvariantCulture,
            $" [{MutedTag}]{ts}[/]  {nameMarkup}  {levelMarkup}  {Markup.Escape(entry.Message)}"));
    }

    private static bool ShouldSuppress(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        if (events.Count > 0)
        {
            return false;
        }

        return IsFunctionsInvocationEnvelope(entry.Message);
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
