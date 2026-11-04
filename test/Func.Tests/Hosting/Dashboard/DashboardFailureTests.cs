// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard;

public class DashboardFailureTests
{
    [Fact]
    public async Task StartupFailure_StillStopsHostAndDisposesEveryOwner()
    {
        InvalidOperationException failure = new("terminal acquisition failed");
        LifetimeRenderer renderer = new() { StartFailure = failure };
        LifecycleStream source = new();
        RecordingSink sink = new();
        DashboardPipeline pipeline = new(new DashboardState(), source, renderer, sink);

        Exception? result = await Record.ExceptionAsync(() => pipeline.RunAsync(CancellationToken.None));

        result.Should().BeSameAs(failure);
        source.Shutdown.Should().BeTrue();
        renderer.Disposed.Should().BeTrue();
        sink.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task IdleInputFailure_IsObservedWithoutWaitingForAnotherHostLog()
    {
        IOException failure = new("terminal input failed");
        LifetimeRenderer renderer = new();
        LifecycleStream source = new();
        DashboardPipeline pipeline = new(new DashboardState(), source, renderer);
        Task<int> run = pipeline.RunAsync(CancellationToken.None);
        await renderer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        renderer.Fault(failure);
        Exception? result = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(5)));

        result.Should().BeSameAs(failure);
        source.Shutdown.Should().BeTrue();
        renderer.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task SummaryFailure_DoesNotSkipRendererOrSinkDisposal()
    {
        IOException failure = new("summary failed");
        LifetimeRenderer renderer = new() { SummaryFailure = failure };
        InMemoryHostEventStream source = new();
        source.Complete();
        RecordingSink sink = new();
        DashboardPipeline pipeline = new(new DashboardState(), source, renderer, sink);

        Exception? result = await Record.ExceptionAsync(() => pipeline.RunAsync(CancellationToken.None));

        result.Should().BeSameAs(failure);
        renderer.Disposed.Should().BeTrue();
        sink.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task CleanupFailure_DoesNotReplacePrimaryFailure_AndIsReportedAfterDisposal()
    {
        InvalidOperationException failure = new("primary");
        LifetimeRenderer renderer = new() { StartFailure = failure, SummaryFailure = new IOException("cleanup") };
        LifecycleStream source = new();
        IInteractionService interaction = Substitute.For<IInteractionService>();
        interaction.When(value => value.WriteWarning(Arg.Any<string>()))
            .Do(_ => renderer.Disposed.Should().BeTrue());
        DashboardPipeline pipeline = new(new DashboardState(), source, renderer, interaction: interaction);

        Exception? result = await Record.ExceptionAsync(() => pipeline.RunAsync(CancellationToken.None));

        result.Should().BeSameAs(failure);
        interaction.Received().WriteWarning("Run cleanup failed: cleanup");
    }

    private sealed class LifetimeRenderer : IDashboardRenderer, IDashboardRendererLifetime
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? StartFailure { get; init; }
        public Exception? SummaryFailure { get; init; }
        public bool Disposed { get; private set; }
        public Task Completion => _completion.Task;

        public Task OnStartAsync(DashboardState state, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return StartFailure is null ? Task.CompletedTask : Task.FromException(StartFailure);
        }

        public void Fault(Exception failure) => _completion.TrySetException(failure);
        public void BeginShutdown(int? hostExitCode = null) { }
        public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public Task OnSummaryAsync(SummaryEvent summary, CancellationToken cancellationToken)
            => SummaryFailure is null ? Task.CompletedTask : Task.FromException(SummaryFailure);
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _completion.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LifecycleStream : IHostEventStream, IHostEventStreamLifecycle
    {
        public bool Shutdown { get; private set; }
        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            yield break;
        }

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task RequestShutdownAsync(CancellationToken cancellationToken)
        {
            Shutdown = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSink : IDashboardEventSink
    {
        public bool Disposed { get; private set; }
        public Task OnEventAsync(HostLogEntry entry, IReadOnlyList<DashboardEvent> events, CancellationToken cancellationToken)
            => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
