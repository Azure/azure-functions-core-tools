// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Azure.Functions.Cli.Console.Terminal;

/// <summary>
/// Bounds input buffering while coalescing adjacent motion, resize, and wheel reports.
/// </summary>
internal sealed class TerminalInputQueue(int capacity = 128)
{
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly object _gate = new();
    private readonly LinkedList<TerminalInputEvent> _items = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;
    private ExceptionDispatchInfo? _error;

    public async Task WriteAsync(TerminalInputEvent input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task changed;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                if (_items.Last is { Value: var last } && CanCombine(last, input))
                {
                    _items.Last.Value = input.Kind == TerminalInputKind.Wheel
                        ? input with { Delta = (int)Math.Clamp((long)last.Delta + input.Delta, int.MinValue, int.MaxValue) }
                        : input;
                    Signal();
                    return;
                }

                if (_items.Count < _capacity)
                {
                    _items.AddLast(input);
                    Signal();
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }

    public async IAsyncEnumerable<TerminalInputEvent> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TerminalInputEvent? item = null;
            Task changed;
            lock (_gate)
            {
                changed = _changed.Task;
                if (_items.First is { } first)
                {
                    item = first.Value;
                    _items.RemoveFirst();
                    Signal();
                }
                else if (_completed)
                {
                    _error?.Throw();
                    yield break;
                }
            }

            if (item is not null)
            {
                yield return item;
            }
            else
            {
                await changed.WaitAsync(cancellationToken);
            }
        }
    }

    public void Complete(Exception? error = null)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            _error = error is null ? null : ExceptionDispatchInfo.Capture(error);
            Signal();
        }
    }

    private static bool CanCombine(TerminalInputEvent previous, TerminalInputEvent next)
    {
        if (previous.Kind != next.Kind)
        {
            return false;
        }

        return next.Kind is TerminalInputKind.PointerMove or TerminalInputKind.Resize
            || (next.Kind == TerminalInputKind.Wheel
                && previous.X == next.X && previous.Y == next.Y
                && Math.Sign(previous.Delta) == Math.Sign(next.Delta));
    }

    private void Signal()
    {
        TaskCompletionSource previous = _changed;
        _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        previous.TrySetResult();
    }
}
