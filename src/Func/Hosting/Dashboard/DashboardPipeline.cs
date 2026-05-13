// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Drives the event-source → state → renderer loop. Owns the high-level
/// lifecycle (start banner, event fan-out, summary on exit) and is the only
/// place that touches the renderer surface.
/// </summary>
internal sealed class DashboardPipeline(
    DashboardState state,
    IHostEventStream source,
    IDashboardRenderer renderer)
{
    private readonly DashboardState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IHostEventStream _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IDashboardRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string exitReason = "sigint";
        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Action? shutdownHandler = null;
        if (_renderer is IDashboardShutdownRequester shutdownRequester)
        {
            shutdownHandler = pipelineCts.Cancel;
            shutdownRequester.ShutdownRequested += shutdownHandler;
        }

        await _renderer.OnStartAsync(_state, pipelineCts.Token);

        try
        {
            await foreach (HostLogEntry entry in _source.ReadAsync(pipelineCts.Token))
            {
                IReadOnlyList<DashboardEvent> events = _state.Observe(entry);
                await _renderer.OnEventAsync(entry, events, pipelineCts.Token);
            }

            exitReason = "source_completed";
        }
        catch (OperationCanceledException) when (pipelineCts.IsCancellationRequested)
        {
            exitReason = "sigint";
        }
        finally
        {
            if (_renderer is IDashboardShutdownRequester requester && shutdownHandler is not null)
            {
                requester.ShutdownRequested -= shutdownHandler;
            }

            SummaryEvent summary = _state.BuildSummary(exitReason, DateTimeOffset.UtcNow);

            // OnSummaryAsync must run even when the pipeline is cancelled so
            // the JSON renderer can emit its terminal `summary` record and
            // the compact renderer can restore the cursor.
            using var summaryCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _renderer.OnSummaryAsync(summary, summaryCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Best effort.
            }

            await _renderer.DisposeAsync();
        }

        return 0;
    }
}
