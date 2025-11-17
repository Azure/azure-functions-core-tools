// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;
using static Azure.Functions.Cli.UnitTests.TestUtilities;

namespace Azure.Functions.Cli.UnitTests;

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

        bool registered = CancelKeyHandler.Register(
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

        Assert.True(registered, "Register should return true on first call.");

        try
        {
            // Act
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());

            // Assert immediate shutdown signal
            Assert.True(
                await WaitForConditionAsync(() => shuttingDownInvoked, TimeSpan.FromSeconds(1)),
                "onShuttingDown was not invoked immediately.");
            Assert.False(gracePeriodInvoked, "onGracePeriodTimeout should not be invoked immediately after shutting down.");

            // Assert delayed grace period signal
            Assert.True(
                await WaitForConditionAsync(() => gracePeriodInvoked, TimeSpan.FromSeconds(5), 500),
                "onGracePeriodTimeout was not invoked after delay.");
        }
        finally
        {
            CancelKeyHandler.Unregister();
        }
    }

    [Fact]
    public void Register_Twice_DoesNotDuplicateHandlers()
    {
        try
        {
            // Arrange
            int callCount = 0;

            bool first = CancelKeyHandler.Register(() => callCount++);
            Assert.True(first, "First Register should return true.");

            bool second = CancelKeyHandler.Register(() => callCount++);
            Assert.False(second, "Second Register should return false (no double-registration).");

            // Act
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());

            // Assert
            Assert.Equal(1, callCount);
        }
        finally
        {
            CancelKeyHandler.Unregister();
        }
    }

    [Fact]
    public void Register_NullOnShuttingDown_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CancelKeyHandler.Register(null));
        Assert.Equal("onShuttingDown", ex.ParamName);
    }

    [Fact]
    public void Register_OnlyShuttingDownCallback_AllowsNullGraceCallback()
    {
        try
        {
            // Act
            CancelKeyHandler.Register(() => { }, onGracePeriodTimeout: null);
            var exception = Record.Exception(() =>
                CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs()));

            // Assert
            Assert.Null(exception);
        }
        finally
        {
            CancelKeyHandler.Unregister();
        }
    }

    private static ConsoleCancelEventArgs CreateFakeCancelEventArgs()
    {
        var constructor = typeof(ConsoleCancelEventArgs)
            .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];

        return (ConsoleCancelEventArgs)constructor.Invoke([ConsoleSpecialKey.ControlC]);
    }
}
