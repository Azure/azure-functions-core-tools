// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Hosting.Events;
using Xunit;

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

        Assert.True(first.Disposed);
        Assert.True(second.Disposed);
        Assert.True(third.Disposed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesRemainingChildrenEvenWhenOneThrows()
    {
        var first = new TrackingEventStream();
        var second = new TrackingEventStream { ThrowOnDispose = true };
        var third = new TrackingEventStream();
        var composite = new CompositeHostEventStream([first, second, third]);

        await Assert.ThrowsAsync<AggregateException>(async () => await composite.DisposeAsync());

        Assert.True(first.Disposed);
        Assert.True(third.Disposed);
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
