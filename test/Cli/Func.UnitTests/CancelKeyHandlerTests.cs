// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.UnitTests.Helpers;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests;

public class CancelKeyHandlerTests
{
    [Fact]
    public void CancelKeyHandler_FirstCancel_ShouldInvokeOnFirstCancel_AndKillChildProcesses()
    {
        try
        {
            // Arrange
            bool firstCancelCalled = false;
            var fakeProcessManager = new TestProcessManager();
            var stubReader = new TestConsoleReader();
            CancelKeyHandler.Register(fakeProcessManager, stubReader, onFirstCancel: () => firstCancelCalled = true);

            // Act
            var e = CreateFakeCancelEventArgs();
            CancelKeyHandler.HandleCancelKeyPress(null, e); // First Ctrl+C

            // Assert
            firstCancelCalled.Should().BeTrue();
            fakeProcessManager.KillChildProcessesCalled.Should().BeTrue();
            fakeProcessManager.KillMainProcessCalled.Should().BeFalse();
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    [Fact]
    public void CancelKeyHandler_SecondCancel_ShouldInvokeOnSecondCancel_AndKillMainProcess()
    {
        try
        {
            // Arrange
            bool secondCancelCalled = false;
            var fakeProcessManager = new TestProcessManager();
            var stubReader = new TestConsoleReader();
            CancelKeyHandler.Register(fakeProcessManager, stubReader, onSecondCancel: () => secondCancelCalled = true);

            // Act
            var e = CreateFakeCancelEventArgs();
            CancelKeyHandler.HandleCancelKeyPress(null, e); // First Ctrl+C
            CancelKeyHandler.HandleCancelKeyPress(null, e); // Second Ctrl+C

            // Assert
            secondCancelCalled.Should().BeTrue();
            fakeProcessManager.KillChildProcessesCalled.Should().BeTrue();
            fakeProcessManager.KillMainProcessCalled.Should().BeTrue();
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    [SkippableFact]
    public void CancelKeyHandler_Windows_FirstCancel_ShouldInvokeFirstOnCancel_AndKillChildProcesses()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip on non-Windows
        }

        try
        {
            bool firstCancelCalled = false;
            var fakeProcessManager = new TestProcessManager();
            var stubReader = new TestConsoleReader();
            CancelKeyHandler.Register(fakeProcessManager, consoleReader: stubReader, onFirstCancel: () => firstCancelCalled = true);

            // Act
            var e = CreateFakeCancelEventArgs();
            CancelKeyHandler.HandleCancelKeyPress(null, e); // First Ctrl+C

            // Assert
            firstCancelCalled.Should().BeTrue();
            fakeProcessManager.KillChildProcessesCalled.Should().BeTrue();
            fakeProcessManager.KillMainProcessCalled.Should().BeFalse();
        }
        finally
        {
            CancelKeyHandler.Dispose();
        }
    }

    [SkippableFact]
    public async Task CancelKeyHandler_Windows_FirstCancelWithQKey_ShouldInvokeOnSecondCancel_AndKillMainProcess()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return; // Skip on non-Windows
        }

        try
        {
            bool secondCancelCalled = false;
            var fakeProcessManager = new TestProcessManager();
            var stubReader = new TestConsoleReader();
            CancelKeyHandler.Register(fakeProcessManager, consoleReader: stubReader, onSecondCancel: () => secondCancelCalled = true);

            var e = CreateFakeCancelEventArgs();
            CancelKeyHandler.HandleCancelKeyPress(null, e); // First Ctrl+C

            // Simulate 'q' key press in the background
            stubReader.SimulateKeyPress(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false));

            // Briefly wait to allow `q` to be processed
            await Task.Delay(50);

            secondCancelCalled.Should().BeTrue();
            fakeProcessManager.KillChildProcessesCalled.Should().BeTrue();
            fakeProcessManager.KillMainProcessCalled.Should().BeTrue();
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
