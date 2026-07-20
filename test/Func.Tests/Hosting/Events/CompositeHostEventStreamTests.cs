// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Events;

namespace Azure.Functions.Cli.Tests.Hosting.Events;

public class CompositeHostEventStreamTests
{
    [Fact]
    public async Task DisposeAsync_DisposesEveryChildStream()
    {
        var first = new TrackingEventStream();
        var second = new TrackingEventStream();
        var third = new TrackingEventStream();
        var composite = new CompositeHostEventStream([first, second, third]);

        await composite.DisposeAsync();

        first.Disposed.Should().BeTrue();
        second.Disposed.Should().BeTrue();
        third.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_DisposesRemainingChildrenEvenWhenOneThrows()
    {
        var first = new TrackingEventStream();
        var second = new TrackingEventStream { ThrowOnDispose = true };
        var third = new TrackingEventStream();
        var composite = new CompositeHostEventStream([first, second, third]);

        await FluentActions.Awaiting(async () => await composite.DisposeAsync()).Should().ThrowAsync<AggregateException>();

        first.Disposed.Should().BeTrue();
        third.Disposed.Should().BeTrue();
    }

    private sealed class TrackingEventStream : IHostEventStream
    {
        public bool Disposed { get; private set; }

        public bool ThrowOnDispose { get; init; }

        public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (ThrowOnDispose)
            {
                throw new InvalidOperationException("boom");
            }

            return ValueTask.CompletedTask;
        }
    }
}
