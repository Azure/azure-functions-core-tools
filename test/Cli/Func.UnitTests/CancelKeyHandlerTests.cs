// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli;
using Xunit;

public class CancelKeyHandlerTests
{
    [Fact]
    public async Task CtrlC_InvokesShuttingDown_ThenGracePeriodTimeout()
    {
        // Arrange
        var shuttingDownInvoked = false;
        var gracePeriodInvoked = false;

        var shuttingDownCts = new CancellationTokenSource();
        var gracePeriodCts = new CancellationTokenSource();

        CancelKeyHandler.Register(
            onShuttingDown: () =>
            {
                shuttingDownInvoked = true;
                shuttingDownCts.Cancel();
            },
            onGracePeriodTimeout: () =>
            {
                gracePeriodInvoked = true;
                gracePeriodCts.Cancel();
            });

        try
        {
            // Act
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());

            // Assert immediate shutdown signal
            var shutdownCalled = await Task.Run(() => shuttingDownCts.Token.WaitHandle.WaitOne(500));
            Assert.True(shutdownCalled, "onShuttingDown was not invoked immediately.");
            Assert.True(shuttingDownInvoked);
            Assert.False(gracePeriodInvoked, "onGracePeriodTimeout should not be invoked immediately after shutting down.");

            // Assert delayed grace period signal
            var graceCalled = await Task.Run(() => gracePeriodCts.Token.WaitHandle.WaitOne(3000));
            Assert.True(graceCalled, "onGracePeriodTimeout was not invoked after delay.");
            Assert.True(gracePeriodInvoked);
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    [Fact]
    public void Register_Twice_DoesNotDuplicateHandlers()
    {
        try
        {
            // Arrange
            int callCount = 0;
            CancelKeyHandler.Register(() => callCount++);

            // Act
            CancelKeyHandler.Register(() => callCount++); // Should be ignored
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());

            // Assert
            Assert.Equal(1, callCount);
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    [Fact]
    public async Task Register_WithNullCallbacks_DoesNotThrow()
    {
        try
        {
            // Act
            CancelKeyHandler.Register();
            var exception = Record.Exception(() =>
                CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs()));

            // Assert
            Assert.Null(exception);

            await Task.Delay(2100);
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    [Fact]
    public async Task MultipleCtrlC_OnlyTriggersHandlersOnce()
    {
        try
        {
            // Arrange
            int shutdownCount = 0;
            int graceCount = 0;

            var graceCts = new CancellationTokenSource();

            CancelKeyHandler.Register(
                onShuttingDown: () => shutdownCount++,
                onGracePeriodTimeout: () =>
                {
                    graceCount++;
                    graceCts.Cancel();
                });

            // Act
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());

            await Task.Run(() => graceCts.Token.WaitHandle.WaitOne(3000));

            // Assert
            Assert.Equal(1, shutdownCount);
            Assert.Equal(1, graceCount);
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    private static ConsoleCancelEventArgs CreateFakeCancelEventArgs()
    {
        var constructor = typeof(ConsoleCancelEventArgs)
            .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];

        return (ConsoleCancelEventArgs)constructor.Invoke([ConsoleSpecialKey.ControlC]);
    }
}
