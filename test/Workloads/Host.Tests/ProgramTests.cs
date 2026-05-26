// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class ProgramTests
{
    [Fact]
    public async Task StartStandardInputClosedMonitorAsync_DoesNotBlockCallerWhenReadLineBlocks()
    {
        using CancellationTokenSource shutdownTokenSource = new();
        var reader = new BlockingTextReader();

        Task monitorTask = Program.StartStandardInputClosedMonitorAsync(reader, shutdownTokenSource);

        await reader.WaitForReadAsync();
        Assert.False(monitorTask.IsCompleted);

        reader.Release();
        await monitorTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(shutdownTokenSource.IsCancellationRequested);
    }

    private sealed class BlockingTextReader : TextReader
    {
        private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitForReadAsync()
            => _readStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void Release()
            => _release.SetResult();

        public override Task<string?> ReadLineAsync()
        {
            _readStarted.SetResult();
            _release.Task.GetAwaiter().GetResult();
            return Task.FromResult<string?>(null);
        }
    }
}
