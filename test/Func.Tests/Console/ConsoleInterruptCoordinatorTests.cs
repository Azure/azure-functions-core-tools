// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Tests.Console;

public class ConsoleInterruptCoordinatorTests
{
    [Fact]
    public void StopThenEscalate_RestoresBeforeForceExit()
    {
        List<string> events = [];
        ConsoleInterruptCoordinator coordinator = new(() => events.Add("cancel"), code => events.Add($"exit {code}"));
        using IDisposable registration = coordinator.RegisterTerminal(() => events.Add("restore"));
        coordinator.RequestStop();
        coordinator.RequestStop();
        events.Should().Equal("cancel", "restore", "exit 130");
    }

    [Fact]
    public void DisposingRegistration_PreventsLaterRestoration()
    {
        int restored = 0;
        ConsoleInterruptCoordinator coordinator = new(() => { }, _ => { });
        IDisposable registration = coordinator.RegisterTerminal(() => restored++);
        registration.Dispose();
        coordinator.RestoreTerminal();
        restored.Should().Be(0);
    }
}
