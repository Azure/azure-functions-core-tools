// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Streaming renderer for non-TTY / CI contexts. No live region, no ANSI
/// colors by default, stable prefix tokens (<c>[host]</c>, <c>[invocation
/// start]</c>, …) so the output is both grep-friendly and human readable.
/// </summary>
internal sealed class PlainRenderer(IInteractionService interaction) : IDashboardRenderer
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
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
        // Suppress the WebJobs invocation envelope ("Executing '...'" /
        // "Executed '...'") because the synthetic events already convey it.
        string? kind = entry.GetAttribute<string>(HostLogAttributeKeys.CliEventKind);
        if (events.Count == 0 && kind is null or CliEventKinds.Log && !IsWebJobsInvocationEnvelope(entry.Message))
        {
            string function = entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName) ?? entry.Category;
            string tag = entry.Level switch
            {
                LogLevel.Error or LogLevel.Critical => "[error]",
                LogLevel.Warning => "[warn]",
                _ => "[log]",
            };
            _interaction.WriteLine($"{FormatTimestamp(entry.Timestamp)}  {tag,-18} {function}  {entry.Message}");
            if (entry.Exception is not null)
            {
                _interaction.WriteLine($"                                 {entry.Exception.GetType().FullName}: {entry.Exception.Message}");
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsWebJobsInvocationEnvelope(string message)
        => !string.IsNullOrEmpty(message) && (
            message.StartsWith("Executing '", StringComparison.Ordinal) ||
            message.StartsWith("Executed '", StringComparison.Ordinal));

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
                _interaction.WriteLine($"{FormatTimestamp(fd.Timestamp)}  [function loaded]  {fd.Function.Name,-22} {fd.Function.TriggerType,-8} {methods}{route}");
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
                if (verb == "failed" && !string.IsNullOrEmpty(inv.ErrorMessage))
                {
                    _interaction.WriteLine($"                                 {inv.ErrorType}: {inv.ErrorMessage}");
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
}
