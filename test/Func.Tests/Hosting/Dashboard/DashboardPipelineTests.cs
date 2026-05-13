// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard;

public class DashboardPipelineTests
{
    [Fact]
    public async Task RunAsync_WhenRendererRequestsShutdown_EmitsSigintSummary()
    {
        var state = new DashboardState();
        var source = new NeverEndingEventStream();
        var renderer = new ShutdownRequestingRenderer();
        var pipeline = new DashboardPipeline(state, source, renderer);

        Task<int> runTask = pipeline.RunAsync(CancellationToken.None);
        await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        renderer.RequestShutdown();

        int exitCode = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, exitCode);
        Assert.NotNull(renderer.Summary);
        Assert.Equal("sigint", renderer.Summary.ExitReason);
        Assert.True(renderer.Disposed);
    }

    private sealed class NeverEndingEventStream : IHostEventStream
    {
        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class ShutdownRequestingRenderer : IDashboardRenderer, IDashboardShutdownRequester
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SummaryEvent? Summary { get; private set; }

        public bool Disposed { get; private set; }

        public event Action? ShutdownRequested;

        public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
        {
            Started.SetResult();
            return Task.CompletedTask;
        }

        public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken)
        {
            Summary = summary;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public void RequestShutdown() => ShutdownRequested?.Invoke();
    }
}
