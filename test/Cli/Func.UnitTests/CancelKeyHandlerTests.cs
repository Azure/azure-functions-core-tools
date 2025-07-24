// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Autofac;
using Azure.Functions.Cli;
using Azure.Functions.Cli.UnitTests.Helpers;
using Xunit;

public class CancelKeyHandlerTests
{
    [Fact]
    public async Task CtrlC_KillsChildImmediately_AndInvokesGracePeriodTimeout()
    {
        // Arrange
        var ctsShuttingDown = new CancellationTokenSource();
        var ctsGracePeriod = new CancellationTokenSource();
        var mockProcessManager = new TestProcessManager();

        CancelKeyHandler.Register(
            processManager: mockProcessManager,
            onShuttingDown: ctsShuttingDown.Cancel,
            onGracePeriodTimeout: ctsGracePeriod.Cancel);

        try
        {
            // Act
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());

            // Assert immediate child process kill
            Assert.True(mockProcessManager.KillChildProcessesCalled);
            Assert.False(ctsGracePeriod.IsCancellationRequested);

            // Assert graceful timeout is called after ~2 seconds
            var gracePeriodInvoked = await Task.Run(() =>
                ctsGracePeriod.Token.WaitHandle.WaitOne(3000)); // Slight buffer

            Assert.True(gracePeriodInvoked, "Grace period timeout was not triggered.");
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    internal static ConsoleCancelEventArgs CreateFakeCancelEventArgs()
    {
        var constructor = typeof(ConsoleCancelEventArgs)
            .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];

        return (ConsoleCancelEventArgs)constructor.Invoke([ConsoleSpecialKey.ControlC]);
    }
}
