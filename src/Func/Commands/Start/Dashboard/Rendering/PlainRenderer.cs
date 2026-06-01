// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Streaming renderer for non-TTY / CI contexts. No live region, no ANSI
/// colors by default, stable prefix tokens (<c>[host]</c>, <c>[invocation
/// start]</c>, …) so the output is both grep-friendly and human readable.
/// </summary>
internal sealed class PlainRenderer(IInteractionService interaction, IAnsiConsole? console = null) : IDashboardRenderer
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IAnsiConsole _console = console ?? AnsiConsole.Console;
    private bool _bannerPrinted;
    private string? _hostVersion;
    private string? _listenUri;

    public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
    {
        _interaction.WriteLine("Azure Functions CLI");
        return Task.CompletedTask;
    }

    public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
    {
        // Opportunistically capture host metadata as it streams in.
        string? version = entry.GetAttribute<string>(HostLogAttributeKeys.HostVersion);
        if (!string.IsNullOrEmpty(version))
        {
            _hostVersion = version;
        }

        string? listen = entry.GetAttribute<string>(HostLogAttributeKeys.HostListenUri);
        if (!string.IsNullOrEmpty(listen))
        {
            _listenUri = listen;
        }

        foreach (DashboardEvent ev in events)
        {
            // The first time the host reaches Ready we promote the buffered
            // host metadata to a banner so the rest of the stream has a
            // clear context block above it.
            if (!_bannerPrinted && ev is HostStateChangedEvent { To: HostLifecycleState.Ready })
            {
                PrintBanner();
            }

            RenderEvent(ev);
        }

        // Plain log lines (no synthetic kind matched, no `cli.event_kind`
        // marker) are still useful — surface them as a generic [log] line.
        // Suppress the WebJobs invocation envelope only when it has no
        // attached exception details; otherwise the legacy experience showed
        // those details and we need to preserve them.
        if (ShouldRenderPlainLog(entry, events))
        {
            string message = FormatLogMessage(entry);
            if (string.IsNullOrWhiteSpace(message))
            {
                return Task.CompletedTask;
            }

            string? function = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName);
            string tag = entry.Level switch
            {
                LogLevel.Error or LogLevel.Critical => "[error]",
                LogLevel.Warning => "[warn]",
                _ => "[log]",
            };
            string line = function is not null
                ? $"{FormatTimestamp(entry.Timestamp)}  {tag,-18} {function}  {message}"
                : $"{FormatTimestamp(entry.Timestamp)}  {tag,-18} {message}";
            _interaction.WriteLine(line);
            if (!string.IsNullOrWhiteSpace(entry.Message) && entry.ExceptionDetails is not null)
            {
                _interaction.WriteLine($"                                 {entry.ExceptionDetails.FormatSummary()}");
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsWebJobsInvocationEnvelope(string message)
        => !string.IsNullOrEmpty(message) && (
            message.StartsWith("Executing '", StringComparison.Ordinal) ||
            message.StartsWith("Executed '", StringComparison.Ordinal));

    private static bool ShouldRenderPlainLog(HostLogEntry entry, IReadOnlyList<DashboardEvent> events)
    {
        string? kind = entry.GetAttribute<string>(HostLogAttributeKeys.CliEventKind);
        if (kind is not (null or CliEventKinds.Log))
        {
            return false;
        }

        if (events.Count > 0)
        {
            return entry.ExceptionDetails is not null && !HasRenderedExceptionDetails(events);
        }

        if (entry.ExceptionDetails is null && IsWebJobsInvocationEnvelope(entry.Message))
        {
            return false;
        }

        return entry.ExceptionDetails is not null || !string.IsNullOrWhiteSpace(entry.Message);
    }

    private static bool HasRenderedExceptionDetails(IReadOnlyList<DashboardEvent> events)
        => events.Any(static ev => ev is InvocationCompletedEvent { Error: not null });

    private static string FormatLogMessage(HostLogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Message))
        {
            return entry.Message;
        }

        return entry.ExceptionDetails?.FormatSummary() ?? string.Empty;
    }

    private void PrintBanner()
    {
        _bannerPrinted = true;
        if (!string.IsNullOrEmpty(_hostVersion))
        {
            _interaction.WriteLine($"Host v{_hostVersion}");
        }

        if (!string.IsNullOrEmpty(_listenUri))
        {
            _interaction.WriteLine($"Listening on {_listenUri}");
        }

        _interaction.WriteBlankLine();
    }

    public Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken)
    {
        var uptime = TimeSpan.FromSeconds(summary.UptimeSeconds);
        string line = string.Create(CultureInfo.InvariantCulture, $"{FormatTimestamp(summary.Timestamp)}  [shutdown]         Stopped after {uptime:hh\\:mm\\:ss} · {summary.FunctionCount} functions · {summary.TotalInvocations} invocations · {summary.ErrorCount} error{(summary.ErrorCount == 1 ? string.Empty : "s")}");
        _interaction.WriteLine(line);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void RenderEvent(DashboardEvent ev)
    {
        switch (ev)
        {
            case FunctionDiscoveredEvent fd:
            {
                string route = !string.IsNullOrEmpty(fd.Function.Route) ? fd.Function.Route : "—";
                string methods = fd.Function.HttpMethods.Count > 0
                    ? string.Join(",", fd.Function.HttpMethods) + " "
                    : string.Empty;

                string displayRoute = methods + route;

                // For HTTP-triggered functions, render the full clickable
                // URL so developers can Ctrl/Cmd-click it to invoke the
                // function — matching the legacy `func start` behavior.
                // OSC 8 hyperlink emission is gated on the terminal's
                // declared capability so we don't leak raw escape bytes
                // into files or pipes.
                bool isHttp = string.Equals(fd.Function.TriggerType, "http", StringComparison.OrdinalIgnoreCase);
                if (isHttp && !string.IsNullOrEmpty(_listenUri) && !string.IsNullOrEmpty(fd.Function.Route))
                {
                    string url = CombineUrl(_listenUri, fd.Function.Route);
                    string clickable = _console.Profile.Capabilities.Links
                        ? Osc8Link(url, url)
                        : url;
                    displayRoute = methods + clickable;
                }

                // Write directly to the underlying TextWriter so the raw
                // OSC 8 sequence reaches the terminal intact and so the
                // line doesn't get hard-wrapped at Spectre's profile
                // width when stdout is redirected.
                _console.Profile.Out.Writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{FormatTimestamp(fd.Timestamp)}  [function loaded]  {fd.Function.Name,-22} {fd.Function.TriggerType,-8} {displayRoute}"));
                break;
            }

            case FunctionRemovedEvent fr:
                _interaction.WriteLine($"{FormatTimestamp(fr.Timestamp)}  [function removed] {fr.Name}");
                break;

            case HostStateChangedEvent hs:
            {
                string desc = (hs.From, hs.To) switch
                {
                    (_, HostLifecycleState.Ready) when hs.DurationMs is { } d => $"Host ready ({(d / 1000.0).ToString("F1", CultureInfo.InvariantCulture)}s)",
                    (_, HostLifecycleState.Ready) => "Host ready",
                    (_, HostLifecycleState.Recycling) when !string.IsNullOrEmpty(hs.Trigger) => $"Recycling (file changed: {hs.Trigger})",
                    (_, HostLifecycleState.Recycling) => "Recycling",
                    (_, HostLifecycleState.Starting) => "Host starting",
                    (_, HostLifecycleState.Stopped) => "Host stopped",
                    _ => $"Host {hs.To.ToString().ToLowerInvariant()}",
                };
                _interaction.WriteLine($"{FormatTimestamp(hs.Timestamp)}  [host]             {desc}");
                break;
            }

            case InvocationStartedEvent inv:
            {
                _interaction.WriteLine($"{FormatTimestamp(inv.Timestamp)}  [invocation start] {inv.Function}  (id={ShortId(inv.InvocationId)})");
                break;
            }

            case InvocationCompletedEvent inv:
            {
                string dur = inv.DurationMs is { } d
                    ? $"{((long)d).ToString(CultureInfo.InvariantCulture)}ms"
                    : "?";
                string verb = string.Equals(inv.Result, "failed", StringComparison.OrdinalIgnoreCase) ? "failed" : "succeeded";
                _interaction.WriteLine($"{FormatTimestamp(inv.Timestamp)}  [invocation end]   {inv.Function}  {verb} in {dur} (id={ShortId(inv.InvocationId)})");
                if (verb == "failed" && !string.IsNullOrEmpty(inv.ErrorSummary))
                {
                    _interaction.WriteLine($"                                 {inv.ErrorSummary}");
                }
                break;
            }

            case CliDiagnosticEvent diag:
                _interaction.WriteLine($"{FormatTimestamp(diag.Timestamp)}  [diagnostic]       {diag.Code}: {diag.Message}");
                if (!string.IsNullOrEmpty(diag.Recommendation))
                {
                    _interaction.WriteLine($"                                 → {diag.Recommendation}");
                }
                break;
        }
    }

    private static string FormatTimestamp(DateTimeOffset ts)
        => ts.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static string ShortId(string id)
        => id.Length > 8 ? id[..8] : id;

    private static string CombineUrl(string baseUrl, string route)
    {
        string left = baseUrl.TrimEnd('/');
        string right = route.StartsWith('/') ? route : "/" + route;
        return left + right;
    }

    // OSC 8 hyperlink: ESC ] 8 ; ; URL ST DISPLAY ESC ] 8 ; ; ST
    // ST = ESC '\'. Recognized by Windows Terminal, VS Code, iTerm2,
    // GNOME Terminal, Konsole, etc. Terminals that don't honor the
    // sequence render the display text and silently ignore the wrapper.
    private static string Osc8Link(string url, string display)
        => $"\u001b]8;;{url}\u001b\\{display}\u001b]8;;\u001b\\";
}
