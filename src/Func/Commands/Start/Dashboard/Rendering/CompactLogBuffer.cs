// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Maintains the bounded compact dashboard log tail.
/// </summary>
internal sealed class CompactLogBuffer(int capacity = CompactLogBuffer.DefaultCapacity, int textBudget = CompactLogBuffer.DefaultTextBudget)
{
    public const int DefaultCapacity = 10_000;
    public const int DefaultTextBudget = 16 * 1024 * 1024;

    private readonly Lock _lock = new();
    private readonly Queue<BufferedLogEntry> _tail = new();
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly int _textBudget = textBudget > 0 ? textBudget : throw new ArgumentOutOfRangeException(nameof(textBudget));
    private long _sequence;
    private long _textBytes;
    private long _revision;
    private long _evicted;

    /// <summary>
    /// Adds display text and returns the oldest remaining ID when eviction invalidates cached projections.
    /// </summary>
    public long? Add(CompactLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_lock)
        {
            line = line.LimitText(_textBudget / sizeof(char));
            if (line.TextBytes > _textBudget)
            {
                line = new CompactLogLine(new Spectre.Console.Text("[display truncated]"), null, line.IsError, line.Level)
                    .LimitText(_textBudget / sizeof(char));
            }

            _tail.Enqueue(new BufferedLogEntry(++_sequence, line));
            _textBytes += line.TextBytes;
            bool evicted = false;
            while (_tail.Count > _capacity || _textBytes > _textBudget)
            {
                _textBytes -= _tail.Dequeue().Line.TextBytes;
                _evicted++;
                evicted = true;
            }

            _revision++;
            return evicted ? _tail.Peek().Id : null;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _tail.Clear();
            _textBytes = 0;
            _evicted = 0;
            _revision++;
        }
    }

    public CompactLogLine[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _tail.Select(entry => entry.Line)];
        }
    }

    public LogBufferSnapshot Capture()
    {
        lock (_lock)
        {
            return new LogBufferSnapshot(_revision, _textBytes, _evicted, [.. _tail]);
        }
    }
}

internal sealed record BufferedLogEntry(long Id, CompactLogLine Line);

internal sealed record LogBufferSnapshot(long Revision, long TextBytes, long Evicted, BufferedLogEntry[] Entries);
