// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Maintains the bounded compact dashboard log tail.
/// </summary>
internal sealed class CompactLogBuffer(int capacity)
{
    public const int DefaultCapacity = 200;

    private readonly Lock _lock = new();
    private readonly Queue<CompactLogLine> _tail = new();
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public void Add(CompactLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_lock)
        {
            _tail.Enqueue(line);
            while (_tail.Count > _capacity)
            {
                _tail.Dequeue();
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _tail.Clear();
        }
    }

    public CompactLogLine[] Snapshot()
    {
        lock (_lock)
        {
            return [.. _tail];
        }
    }
}
