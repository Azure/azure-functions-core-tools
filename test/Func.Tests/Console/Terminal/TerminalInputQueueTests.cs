// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console.Terminal;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public class TerminalInputQueueTests
{
    [Fact]
    public async Task AdjacentMotionResizeAndWheel_AreCoalescedWithoutReorderingButtonsOrKeys()
    {
        TerminalInputQueue queue = new();
        TerminalInputEvent[] input =
        [
            new(TerminalInputKind.PointerDown, X: 1),
            new(TerminalInputKind.PointerMove, X: 2),
            new(TerminalInputKind.PointerMove, X: 3),
            new(TerminalInputKind.PointerUp, X: 3),
            new(TerminalInputKind.Resize),
            new(TerminalInputKind.Resize),
            new(TerminalInputKind.Wheel, Delta: 3),
            new(TerminalInputKind.Wheel, Delta: 6),
            new(TerminalInputKind.Wheel, Delta: -3),
            TerminalKey.Create('\u0003'),
        ];
        foreach (TerminalInputEvent item in input)
        {
            await queue.WriteAsync(item, CancellationToken.None);
        }

        queue.Complete();
        List<TerminalInputEvent> received = [];
        await foreach (TerminalInputEvent item in queue.ReadAsync(CancellationToken.None))
        {
            received.Add(item);
        }

        received.Should().Equal(input[0], input[2], input[3], input[4],
            new TerminalInputEvent(TerminalInputKind.Wheel, Delta: 9), input[8], input[9]);
    }

    [Fact]
    public async Task Capacity_AppliesCancellableBackpressureWithoutDroppingKeys()
    {
        TerminalInputQueue queue = new(1);
        await queue.WriteAsync(TerminalKey.Create('q'), CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        Task blocked = queue.WriteAsync(TerminalKey.Create('c'), cancellation.Token);
        blocked.IsCompleted.Should().BeFalse();
        cancellation.Cancel();
        Func<Task> write = () => blocked;
        await write.Should().ThrowAsync<OperationCanceledException>();
        queue.Complete();
        await using var reader = queue.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        (await reader.MoveNextAsync()).Should().BeTrue();
        reader.Current.Key.KeyChar.Should().Be('q');
        (await reader.MoveNextAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task FailureCompletion_IsNotOverwrittenByLaterSuccessfulCompletion()
    {
        TerminalInputQueue queue = new();
        queue.Complete(new IOException("read failed"));
        queue.Complete();
        await using var reader = queue.ReadAsync(CancellationToken.None).GetAsyncEnumerator();
        Func<Task> read = async () => await reader.MoveNextAsync();
        await read.Should().ThrowAsync<IOException>().WithMessage("read failed");
    }
}
