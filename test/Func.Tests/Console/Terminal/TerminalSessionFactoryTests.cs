// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Console.Terminal;
using NSubstitute;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public class TerminalSessionFactoryTests
{
    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void UnsupportedOutput_DoesNotAcquireNativeInput(bool ansi, bool interactive, bool alternate, bool terminal)
    {
        IAnsiConsole console = Console(ansi, interactive, alternate, terminal);
        ITerminalDriverFactory drivers = Substitute.For<ITerminalDriverFactory>();
        TerminalSessionFactory factory = new(Substitute.For<IPlatform>(), drivers);
        factory.IsSupported(console).Should().BeFalse();
        drivers.DidNotReceive().Create();
    }

    [Fact]
    public void SupportedProbe_DoesNotActivateOrWriteProtocols()
    {
        IAnsiConsole console = Console();
        ITerminalDriver driver = Substitute.For<ITerminalDriver>();
        driver.IsSupported.Returns(true);
        driver.Size.Returns(new TerminalSize(80, 24));
        ITerminalDriverFactory drivers = Substitute.For<ITerminalDriverFactory>();
        drivers.Create().Returns(driver);
        TerminalSessionFactory factory = new(Substitute.For<IPlatform>(), drivers);

        factory.IsSupported(console).Should().BeTrue();
        driver.DidNotReceive().Activate();
        console.Profile.Out.Writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public void WindowsManagedHandoff_TransfersPendingKeysBeforeNativeOwnership()
    {
        IPlatform platform = Substitute.For<IPlatform>();
        platform.IsWindows.Returns(true);
        IAnsiConsole console = Console();
        console.Input.IsKeyAvailable().Returns(true, true, false);
        console.Input.ReadKey(true).Returns(new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false),
            new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true));
        ITerminalDriverFactory drivers = Substitute.For<ITerminalDriverFactory>();
        TerminalSessionFactory factory = new(platform, drivers);

        factory.CanHandoffManagedInput.Should().BeTrue();
        factory.DrainManagedInput(console, CancellationToken.None).Should().Equal(TerminalKey.Create('q'), TerminalKey.Create('\u0003'));
        drivers.DidNotReceive().Create();
    }

    [Fact]
    public void WindowsManagedHandoff_EmitsOneCompleteTextEventForSurrogatePair()
    {
        IPlatform platform = Windows();
        IAnsiConsole console = Console();
        console.Input.IsKeyAvailable().Returns(true, true, true, true, false);
        console.Input.ReadKey(true).Returns(ManagedKey('/'), ManagedKey('\ud83d'), ManagedKey('\ude00'), ManagedKey('q'));
        TerminalSessionFactory factory = new(platform);

        factory.DrainManagedInput(console, CancellationToken.None).Should().Equal(
            TerminalKey.Create('/'), new TerminalInputEvent(TerminalInputKind.Text, Text: "😀"), TerminalKey.Create('q'));
    }

    [Fact]
    public void WindowsManagedHandoff_RetainsHighSurrogateAcrossSeparateDrainsWithoutWaiting()
    {
        IAnsiConsole console = Console();
        console.Input.IsKeyAvailable().Returns(true, false);
        console.Input.ReadKey(true).Returns(ManagedKey('\ud83d'));
        TerminalSessionFactory factory = new(Windows());

        factory.DrainManagedInput(console, CancellationToken.None).Should().BeEmpty();

        console.Input.IsKeyAvailable().Returns(true, false);
        console.Input.ReadKey(true).Returns(ManagedKey('\ude00'));
        factory.DrainManagedInput(console, CancellationToken.None)
            .Should().Equal(new TerminalInputEvent(TerminalInputKind.Text, Text: "😀"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WindowsManagedHandoff_RetainsCachedHighSurrogateUntilNativeLowSurrogateArrives(bool cancelAfterHigh)
    {
        using CancellationTokenSource drainCancellation = new();
        IAnsiConsole console = Console();
        console.Input.IsKeyAvailable().Returns(true, false);
        console.Input.ReadKey(true).Returns(_ =>
        {
            if (cancelAfterHigh)
            {
                drainCancellation.Cancel();
            }

            return ManagedKey('\ud83d');
        });

        using ManualResetEventSlim deliverLow = new();
        TaskCompletionSource polled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IWindowsTerminalNative native = Substitute.For<IWindowsTerminalNative>();
        native.IsAvailable.Returns(true);
        native.GetWindow().Returns(new WindowsConsoleWindow(0, 0, 80, 24));
        native.Read().Returns(new WindowsInputRecord
        {
            EventType = 1,
            Key = new WindowsKeyRecord { KeyDown = 1, RepeatCount = 1, UnicodeCharacter = '\ude00' },
        });
        int delivered = 0;
        native.WaitForInput(Arg.Any<CancellationToken>()).Returns(call =>
        {
            CancellationToken token = call.Arg<CancellationToken>();
            token.ThrowIfCancellationRequested();
            if (deliverLow.IsSet && Interlocked.CompareExchange(ref delivered, 1, 0) == 0)
            {
                return true;
            }

            token.WaitHandle.WaitOne(10);
            token.ThrowIfCancellationRequested();
            polled.TrySetResult();
            return false;
        });
        ITerminalDriverFactory drivers = Substitute.For<ITerminalDriverFactory>();
        drivers.Create(Arg.Any<WindowsInputNormalizer?>()).Returns(call =>
            new WindowsTerminalDriver(native, call.Arg<WindowsInputNormalizer?>()));
        TerminalSessionFactory factory = new(Windows(), drivers);

        if (cancelAfterHigh)
        {
            Action drain = () => factory.DrainManagedInput(console, drainCancellation.Token).ToArray();
            drain.Should().Throw<OperationCanceledException>();
        }
        else
        {
            factory.DrainManagedInput(console, drainCancellation.Token).Should().BeEmpty();
        }

        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        await using ITerminalSession session = factory.Open(console);
        await using var reader = session.ReadAsync(deadline.Token).GetAsyncEnumerator();
        Task<bool> next = reader.MoveNextAsync().AsTask();
        await polled.Task.WaitAsync(deadline.Token);
        next.IsCompleted.Should().BeFalse("a pending high surrogate must not be emitted or replaced on an idle native poll");
        deliverLow.Set();

        (await next.WaitAsync(deadline.Token)).Should().BeTrue();
        reader.Current.Should().Be(new TerminalInputEvent(TerminalInputKind.Text, Text: "😀"));
        console.Input.Received(1).ReadKey(true);
        native.Received(1).Read();
    }

    [Fact]
    public void WindowsManagedHandoff_CancellationBeforeReadLeavesTypeaheadUntouched()
    {
        IAnsiConsole console = Console();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        TerminalSessionFactory factory = new(Windows());

        Action drain = () => factory.DrainManagedInput(console, cancellation.Token).ToArray();

        drain.Should().Throw<OperationCanceledException>();
        console.Input.DidNotReceive().IsKeyAvailable();
        console.Input.DidNotReceive().ReadKey(Arg.Any<bool>());
    }

    [Fact]
    public void WindowsManagedHandoff_UnpairedSurrogateDoesNotDiscardTheFollowingKey()
    {
        IAnsiConsole console = Console();
        console.Input.IsKeyAvailable().Returns(true, true, false);
        console.Input.ReadKey(true).Returns(ManagedKey('\ud83d'), ManagedKey('q'));
        TerminalSessionFactory factory = new(Windows());

        factory.DrainManagedInput(console, CancellationToken.None).Should().Equal(
            new TerminalInputEvent(TerminalInputKind.Text, Text: "\ufffd"), TerminalKey.Create('q'));
    }

    [Fact]
    public void WindowsManagedHandoff_DoesNotReparseAlreadyDecodedEscapeOrControlKeys()
    {
        IAnsiConsole console = Console();
        console.Input.IsKeyAvailable().Returns(true, true, false);
        console.Input.ReadKey(true).Returns(TerminalKey.Create('\u001b').Key, TerminalKey.Create('\u0003').Key);
        TerminalSessionFactory factory = new(Windows());

        factory.DrainManagedInput(console, CancellationToken.None).Should().Equal(
            TerminalKey.Create('\u001b'), TerminalKey.Create('\u0003'));
    }

    [Fact]
    public void UnixManagedHandoff_IsExplicitlyUnsupportedRatherThanFlushingHiddenDecoderState()
    {
        TerminalSessionFactory factory = new(Substitute.For<IPlatform>());
        factory.CanHandoffManagedInput.Should().BeFalse();
        Action handoff = () => factory.DrainManagedInput(Console(), CancellationToken.None).ToArray();
        handoff.Should().Throw<PlatformNotSupportedException>();
    }

    [Fact]
    public void NullDependencies_AreRejected()
    {
        Action construct = () => new TerminalSessionFactory(null!);
        construct.Should().Throw<ArgumentNullException>();
    }

    private static IPlatform Windows()
    {
        IPlatform platform = Substitute.For<IPlatform>();
        platform.IsWindows.Returns(true);
        return platform;
    }

    private static ConsoleKeyInfo ManagedKey(char character) => new(character, default, false, false, false);

    private static IAnsiConsole Console(bool ansi = true, bool interactive = true, bool alternate = true, bool terminal = true)
    {
        IAnsiConsoleOutput output = Substitute.For<IAnsiConsoleOutput>();
        output.IsTerminal.Returns(terminal);
        output.Width.Returns(80);
        output.Height.Returns(24);
        output.Writer.Returns(new StringWriter());
        Profile profile = new(output, Encoding.UTF8);
        profile.Capabilities.Ansi = ansi;
        profile.Capabilities.Interactive = interactive;
        profile.Capabilities.AlternateBuffer = alternate;
        IAnsiConsole console = Substitute.For<IAnsiConsole>();
        console.Profile.Returns(profile);
        console.Input.Returns(Substitute.For<IAnsiConsoleInput>());
        return console;
    }
}
