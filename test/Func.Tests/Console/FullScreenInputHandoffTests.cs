// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.CompilerServices;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Terminal;
using Azure.Functions.Cli.Console.Theme;
using NSubstitute;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Console;

public class FullScreenInputHandoffTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PromptInput_RequiresALosslessHandoff(bool canHandoff)
    {
        using BufferedConsole console = new(interactive: true, ansi: true);
        console.Enqueue(ConsoleKey.Y, 'y');
        console.Enqueue(ConsoleKey.Enter, '\r');
        SpectreInteractionService interaction = new(new DefaultTheme(), console, console);
        ITerminalSessionFactory factory = Substitute.For<ITerminalSessionFactory, ITerminalInputHandoff>();
        factory.IsSupported(console).Returns(true);
        ((ITerminalInputHandoff)factory).CanHandoffManagedInput.Returns(canHandoff);

        interaction.CanUseFullScreen(factory).Should().BeTrue();
        await interaction.ConfirmAsync("Continue?", cancellationToken: CancellationToken.None);

        interaction.CanUseFullScreen(factory).Should().Be(canHandoff);
        if (!canHandoff)
        {
            FluentActions.Invoking(() => interaction.OpenFullScreen(factory)).Should().Throw<PlatformNotSupportedException>();
            factory.DidNotReceive().Open(Arg.Any<IAnsiConsole>());
        }
    }

    [Fact]
    public async Task HandoffKeys_AreDeliveredBeforeNativeInput()
    {
        using BufferedConsole console = new(interactive: true, ansi: true);
        console.Enqueue(ConsoleKey.Y, 'y');
        console.Enqueue(ConsoleKey.Enter, '\r');
        SpectreInteractionService interaction = new(new DefaultTheme(), console, console);
        ITerminalSessionFactory factory = Substitute.For<ITerminalSessionFactory, ITerminalInputHandoff>();
        ITerminalSession inner = Substitute.For<ITerminalSession>();
        factory.IsSupported(console).Returns(true);
        factory.Open(console).Returns(inner);
        var handoff = (ITerminalInputHandoff)factory;
        handoff.CanHandoffManagedInput.Returns(true);
        handoff.DrainManagedInput(console, Arg.Any<CancellationToken>())
            .Returns([new TerminalInputEvent(TerminalInputKind.Key, new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false))]);
        inner.ReadAsync(Arg.Any<CancellationToken>()).Returns(call => NativeInput(call.Arg<CancellationToken>()));
        await interaction.ConfirmAsync("Continue?", cancellationToken: CancellationToken.None);

        await using ITerminalSession session = interaction.OpenFullScreen(factory);
        List<char> keys = [];
        await foreach (TerminalInputEvent input in session.ReadAsync(CancellationToken.None))
        {
            keys.Add(input.Key.KeyChar);
        }

        keys.Should().Equal('a', 'b');
        Received.InOrder(() =>
        {
            handoff.DrainManagedInput(console, Arg.Any<CancellationToken>());
            factory.Open(console);
        });
    }

    [Fact]
    public void CanceledAcquisition_DoesNotOpenNativeInput()
    {
        using BufferedConsole console = new(interactive: true, ansi: true);
        SpectreInteractionService interaction = new(new DefaultTheme(), console, console);
        ITerminalSessionFactory factory = Substitute.For<ITerminalSessionFactory>();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        FluentActions.Invoking(() => interaction.OpenFullScreen(factory, cancellation.Token))
            .Should().Throw<OperationCanceledException>();

        factory.DidNotReceive().Open(Arg.Any<IAnsiConsole>());
    }

    private static async IAsyncEnumerable<TerminalInputEvent> NativeInput([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new TerminalInputEvent(TerminalInputKind.Key, new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));
        await Task.CompletedTask;
    }
}
