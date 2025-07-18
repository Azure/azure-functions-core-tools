// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Interfaces;
using FluentAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests;

public class CancelKeyHandlerTests
{
    [Fact]
    public void FirstCtrlC_ShouldInvokeOnFirstCancel_AndKillChildProcesses()
    {
        try
        {
            // Arrange
            bool firstCancelCalled = false;
            var fakeProcessManager = new FakeProcessManager();
            CancelKeyHandler.Register(fakeProcessManager, onFirstCancel: () => firstCancelCalled = true);

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
    public void SecondCtrlC_ShouldInvokeOnSecondCancel_AndKillMainProcess()
    {
        try
        {
            // Arrange
            bool secondCancelCalled = false;
            var fakeProcessManager = new FakeProcessManager();
            CancelKeyHandler.Register(fakeProcessManager, onSecondCancel: () => secondCancelCalled = true);

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

    private static ConsoleCancelEventArgs CreateFakeCancelEventArgs()
    {
        var constructor = typeof(ConsoleCancelEventArgs)
            .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];

        return (ConsoleCancelEventArgs)constructor.Invoke([ConsoleSpecialKey.ControlC]);
    }
}

internal class FakeProcessManager : IProcessManager
{
    public bool KillChildProcessesCalled { get; private set; }

    public bool KillMainProcessCalled { get; private set; }

    public void KillChildProcesses() => KillChildProcessesCalled = true;

    public void KillMainProcess() => KillMainProcessCalled = true;

    // Other interface members omitted for this test
    public IEnumerable<IProcessInfo> GetProcessesByName(string processName) => null;

    public IProcessInfo GetCurrentProcess() => null;

    public IProcessInfo GetProcessById(int processId) => null;

    public bool RegisterChildProcess(System.Diagnostics.Process childProcess) => false;
}
