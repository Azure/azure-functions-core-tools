// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli;
using Xunit;
using static Azure.Functions.Cli.UnitTests.TestUtilities;

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
            Assert.True(await WaitForConditionAsync(() => shuttingDownInvoked, TimeSpan.FromSeconds(1)), "onShuttingDown was not invoked immediately.");
            Assert.False(gracePeriodInvoked, "onGracePeriodTimeout should not be invoked immediately after shutting down.");

            // Assert delayed grace period signal
            Assert.True(await WaitForConditionAsync(() => gracePeriodInvoked, TimeSpan.FromSeconds(5), 500), "onGracePeriodTimeout was not invoked after delay.");
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
    public void Register_WithNullCallbacks_DoesNotThrow()
    {
        try
        {
            // Act
            CancelKeyHandler.Register();
            var exception = Record.Exception(() =>
                CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs()));

            // Assert
            Assert.Null(exception);
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
