// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Autofac;
using Azure.Functions.Cli;
using Azure.Functions.Cli.UnitTests.Helpers;
using Xunit;

public class CancelKeyHandlerTests
{
    [Fact]
    public async Task CtrlC_KillsChildImmediately_AndMainAfterDelay()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var mockProcessManager = new TestProcessManager(); // your concrete test double

        CancelKeyHandler.Register(mockProcessManager, onCancel: () => cts.Cancel());

        try
        {
            // Act
            CancelKeyHandler.HandleCancelKeyPress(null, CreateFakeCancelEventArgs());

            // Assert after short delay
            Assert.True(mockProcessManager.KillChildProcessesCalled);
            Assert.False(mockProcessManager.KillMainProcessCalled);

            await Task.Delay(2200); // Wait for delayed KillMainProcess
            Assert.True(mockProcessManager.KillMainProcessCalled);
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
