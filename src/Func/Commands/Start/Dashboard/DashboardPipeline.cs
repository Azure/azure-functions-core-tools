// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.ExceptionServices;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Hosting.Dashboard;

/// <summary>
/// Owns event fan-out and renderer/host cleanup, including failures before the first frame.
/// </summary>
internal sealed class DashboardPipeline(
    DashboardState state, IHostEventStream source, IDashboardRenderer renderer,
    IDashboardEventSink? eventSink = null, IInteractionService? interaction = null)
{
    private readonly DashboardState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IHostEventStream _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly IDashboardRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly IDashboardEventSink? _eventSink = eventSink;
    private readonly IInteractionService? _interaction = interaction;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string exitReason = "sigint";
        int exitCode = 0;
        int? hostExitCode = null;
        ExceptionDispatchInfo? failure = null;
        List<Exception> cleanupFailures = [];
        using var pipelineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var requester = _renderer as IDashboardShutdownRequester;
        var lifetime = _renderer as IDashboardRendererLifetime;
        Action shutdown = pipelineCts.Cancel;
        Task? consumption = null;
        if (requester is not null)
        {
            requester.ShutdownRequested += shutdown;
        }

        try
        {
            await _renderer.OnStartAsync(_state, pipelineCts.Token);
            consumption = ConsumeAsync(pipelineCts.Token);
            if (lifetime is not null)
            {
                Task completed = await Task.WhenAny(consumption, lifetime.Completion);
                if (completed != consumption)
                {
                    await completed;
                    throw new EndOfStreamException("The dashboard closed before the host stopped.");
                }
            }

            await consumption;
            exitReason = "source_completed";
            if (_source is IHostEventStreamLifecycle host)
            {
                hostExitCode = await host.WaitForExitAsync(pipelineCts.Token);
                exitCode = hostExitCode.Value;
            }
        }
        catch (OperationCanceledException) when (pipelineCts.IsCancellationRequested)
        {
            exitReason = "sigint";
        }
        catch (Exception exception)
        {
            // This lifetime boundary preserves the original failure while every owner is cleaned up.
            exitReason = "host_failed";
            failure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            await AttemptCleanupAsync(() =>
            {
                pipelineCts.Cancel();
                return Task.CompletedTask;
            }, cleanupFailures);
            await AttemptCleanupAsync(() =>
            {
                lifetime?.BeginShutdown(hostExitCode);
                return Task.CompletedTask;
            }, cleanupFailures);
            if (consumption is { IsCompleted: false })
            {
                try
                {
                    await consumption;
                }
                catch (OperationCanceledException) when (pipelineCts.IsCancellationRequested)
                {
                    // Cancellation stops the event consumer before the renderer is disposed.
                }
                catch (Exception exception)
                {
                    failure ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            if (_source is IHostEventStreamLifecycle host)
            {
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(10));
                await AttemptCleanupAsync(() => host.RequestShutdownAsync(cleanup.Token), cleanupFailures);
            }

            if (requester is not null)
            {
                requester.ShutdownRequested -= shutdown;
            }

            SummaryEvent summary = _state.BuildSummary(exitReason, DateTimeOffset.UtcNow);
            using CancellationTokenSource summaryCts = new(TimeSpan.FromSeconds(5));
            await AttemptCleanupAsync(() => _renderer.OnSummaryAsync(summary, summaryCts.Token), cleanupFailures);
            await AttemptCleanupAsync(() => _renderer.DisposeAsync().AsTask(), cleanupFailures);
            if (_eventSink is not null)
            {
                await AttemptCleanupAsync(() => _eventSink.DisposeAsync().AsTask(), cleanupFailures);
            }
        }

        if (failure is not null)
        {
            foreach (Exception cleanupFailure in cleanupFailures.Where(exception => !ReferenceEquals(exception, failure.SourceException)))
            {
                _interaction?.WriteWarning($"Run cleanup failed: {cleanupFailure.Message}");
            }

            failure.Throw();
        }

        if (cleanupFailures.Count > 0)
        {
            ExceptionDispatchInfo.Capture(cleanupFailures[0]).Throw();
        }

        return exitCode;
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        await foreach (HostLogEntry entry in _source.ReadAsync(cancellationToken))
        {
            IReadOnlyList<DashboardEvent> events = _state.Observe(entry);
            if (_eventSink is not null)
            {
                await _eventSink.OnEventAsync(entry, events, cancellationToken);
            }

            await _renderer.OnEventAsync(entry, events, cancellationToken);
        }
    }

    private static async Task AttemptCleanupAsync(Func<Task> cleanup, List<Exception> failures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            // Defer reporting until the remaining owners, particularly the terminal, are restored.
            failures.Add(exception);
        }
    }
}
