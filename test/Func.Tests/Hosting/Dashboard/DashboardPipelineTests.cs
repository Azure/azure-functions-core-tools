// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

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

        exitCode.Should().Be(0);
        renderer.Summary.Should().NotBeNull();
        renderer.Summary.ExitReason.Should().Be("sigint");
        renderer.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenSourceCompletes_ReturnsLifecycleExitCode()
    {
        var state = new DashboardState();
        var source = new CompletedLifecycleEventStream(exitCode: 42);
        var renderer = new ShutdownRequestingRenderer();
        var pipeline = new DashboardPipeline(state, source, renderer);

        int exitCode = await pipeline.RunAsync(CancellationToken.None);

        exitCode.Should().Be(42);
        source.WaitForExitCalled.Should().BeTrue();
        renderer.Summary.Should().NotBeNull();
        renderer.Summary.ExitReason.Should().Be("source_completed");
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

        exitCode.Should().Be(0);
        source.ShutdownRequested.Should().BeTrue();
        renderer.Summary.Should().NotBeNull();
        renderer.Summary.ExitReason.Should().Be("sigint");
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
        exitCode.Should().Be(0);
        output.Should().Contain("[error] Function.HttpTrigger1: Invocation failed");
        output.Should().Contain("invocation_completed HttpTrigger1 invocation-1 failed duration_ms=42");
    }

    [Fact]
    public async Task RunAsync_WhenRendererThrows_StillShutsDownHostAndRethrows()
    {
        var state = new DashboardState();
        var source = new NeverEndingLifecycleEventStream();
        var renderer = new ThrowingRenderer();
        var pipeline = new DashboardPipeline(state, source, renderer);

        // Triggers a non-OCE throw from the renderer, the path that previously skipped shutdown.
        source.PushEntry(new HostLogEntry(
            DateTimeOffset.UnixEpoch,
            "Function.HttpTrigger1",
            LogLevel.Information,
            default,
            "trigger",
            Exception: null,
            HostLogEntry.EmptyAttributes));

        await FluentActions.Awaiting(() => pipeline.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5))).Should().ThrowAsync<InvalidOperationException>();

        source.ShutdownRequested.Should().BeTrue();
    }

    private sealed class ThrowingRenderer : IDashboardRenderer
    {
        public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
            => throw new InvalidOperationException("renderer blew up");

        public Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NeverEndingEventStream : IHostEventStream
    {
        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CompletedLifecycleEventStream(int exitCode) : IHostEventStream, IHostEventStreamLifecycle
    {
        public bool WaitForExitCalled { get; private set; }

        public bool ShutdownRequested { get; private set; }

        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task RequestShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownRequested = true;
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitForExitCalled = true;
            return Task.FromResult(exitCode);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NeverEndingLifecycleEventStream : IHostEventStream, IHostEventStreamLifecycle
    {
        private readonly InMemoryHostEventStream _inner = new();

        public bool ShutdownRequested { get; private set; }

        public void PushEntry(HostLogEntry entry) => _inner.Write(entry);

        public IAsyncEnumerable<HostLogEntry> ReadAsync(CancellationToken cancellationToken)
            => _inner.ReadAsync(cancellationToken);

        public Task RequestShutdownAsync(CancellationToken cancellationToken)
        {
            ShutdownRequested = true;
            _inner.Complete();
            return Task.CompletedTask;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken)
            => Task.FromResult(99);

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
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
