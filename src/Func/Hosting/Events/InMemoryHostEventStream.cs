// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Azure.Functions.Cli.Hosting.Events;

/// <summary>
/// In-memory fake <see cref="IHostEventStream"/> backed by a channel. Tests
/// and the scripted demo source push records via <see cref="Write"/>;
/// consumers read them through <see cref="ReadAsync"/>. Call
/// <see cref="Complete"/> to signal end of stream.
/// </summary>
internal sealed class InMemoryHostEventStream : IHostEventStream
{
    private readonly Channel<HostLogEntry> _channel = Channel.CreateUnbounded<HostLogEntry>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public bool TryWrite(HostLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _channel.Writer.TryWrite(entry);
    }

    public void Write(HostLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_channel.Writer.TryWrite(entry))
        {
            throw new InvalidOperationException("Channel is closed.");
        }
    }

    public void Complete() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<HostLogEntry> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out HostLogEntry? entry))
            {
                yield return entry;
            }
        }
    }
}
