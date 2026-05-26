// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task RunAsync_WhenSourceCompletes_ReturnsLifecycleExitCode()
    {
        var state = new DashboardState();
        var source = new CompletedLifecycleEventStream(exitCode: 42);
        var renderer = new ShutdownRequestingRenderer();
        var pipeline = new DashboardPipeline(state, source, renderer);

        int exitCode = await pipeline.RunAsync(CancellationToken.None);

        Assert.Equal(42, exitCode);
        Assert.True(source.WaitForExitCalled);
        Assert.NotNull(renderer.Summary);
        Assert.Equal("source_completed", renderer.Summary.ExitReason);
    }

    [Fact]
    public async Task RunAsync_WhenRendererRequestsShutdown_RequestsLifecycleShutdownAndReturnsZero()
    {
        var state = new DashboardState();
        var source = new NeverEndingLifecycleEventStream();
        var renderer = new ShutdownRequestingRenderer();
        var pipeline = new DashboardPipeline(state, source, renderer);

        Task<int> runTask = pipeline.RunAsync(CancellationToken.None);
        await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        renderer.RequestShutdown();

        int exitCode = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, exitCode);
        Assert.True(source.ShutdownRequested);
        Assert.NotNull(renderer.Summary);
        Assert.Equal("sigint", renderer.Summary.ExitReason);
    }

    [Fact]
    public async Task RunAsync_WithEventSink_MirrorsRawAndDerivedEvents()
    {
        var state = new DashboardState();
        var source = new InMemoryHostEventStream();
        source.Write(new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Function.HttpTrigger1",
            LogLevel.Error,
            default,
            "Invocation failed",
            null,
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "invocation-1",
                [HostLogAttributeKeys.FunctionResult] = "failed",
                [HostLogAttributeKeys.DurationMs] = 42d,
            }));
        source.Complete();
        var writer = new StringWriter();
        var sink = new DashboardLogFileSink(writer);
        var renderer = new ShutdownRequestingRenderer();
        var pipeline = new DashboardPipeline(state, source, renderer, sink);

        int exitCode = await pipeline.RunAsync(CancellationToken.None);

        string output = writer.ToString();
        Assert.Equal(0, exitCode);
        Assert.Contains("[error] Function.HttpTrigger1: Invocation failed", output);
        Assert.Contains("invocation_completed HttpTrigger1 invocation-1 failed duration_ms=42", output);
    }

    private sealed class NeverEndingEventStream : IHostEventStream
    {
        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }
    }

    private sealed class CompletedLifecycleEventStream(int exitCode) : IHostEventStream, IHostEventStreamLifecycle
    {
        public bool WaitForExitCalled { get; private set; }

        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task RequestShutdownAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitForExitCalled = true;
            return Task.FromResult(exitCode);
        }
    }

    private sealed class NeverEndingLifecycleEventStream : IHostEventStream, IHostEventStreamLifecycle
    {
        public bool ShutdownRequested { get; private set; }

        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public Task RequestShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownRequested = true;
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
            => Task.FromResult(99);
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
