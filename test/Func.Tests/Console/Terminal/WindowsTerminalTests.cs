// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Console.Terminal;
using NSubstitute;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public class WindowsTerminalTests
{
    [Fact]
    public void NativeStructures_MatchWindowsConsoleAbi()
    {
        Marshal.SizeOf<WindowsInputRecord>().Should().Be(20);
        Marshal.SizeOf<WindowsKeyRecord>().Should().Be(16);
        Marshal.SizeOf<WindowsMouseRecord>().Should().Be(16);
        Marshal.SizeOf<WindowsScreenBufferInfo>().Should().Be(22);
        Marshal.SizeOf<WindowsCursorInfo>().Should().Be(8);
        Marshal.OffsetOf<WindowsInputRecord>(nameof(WindowsInputRecord.Key)).ToInt32().Should().Be(4);
        Marshal.OffsetOf<WindowsKeyRecord>(nameof(WindowsKeyRecord.UnicodeCharacter)).ToInt32().Should().Be(10);
        Marshal.OffsetOf<WindowsKeyRecord>(nameof(WindowsKeyRecord.ControlKeyState)).ToInt32().Should().Be(12);
    }

    [Fact]
    public void Wheel_NormalizesRemaindersAndViewportCoordinates()
    {
        WindowsInputNormalizer normalizer = new();
        WindowsConsoleWindow window = new(10, 20, 80, 24);
        normalizer.Decode(Wheel(60), window).Should().Equal(new TerminalInputEvent(TerminalInputKind.Wheel, X: 2, Y: 3, Delta: 1));
        normalizer.Decode(Wheel(60), window).Should().Equal(new TerminalInputEvent(TerminalInputKind.Wheel, X: 2, Y: 3, Delta: 2));
        normalizer.Decode(Wheel(-30), window).Should().BeEmpty();
        normalizer.Decode(Wheel(-210), window).Should().Equal(new TerminalInputEvent(TerminalInputKind.Wheel, X: 2, Y: 3, Delta: -6));
    }

    [Fact]
    public void Keys_PreserveRepeatsModifiersAndRawControlC()
    {
        WindowsInputNormalizer normalizer = new();
        var events = normalizer.Decode(new WindowsInputRecord
        {
            EventType = 1,
            Key = new WindowsKeyRecord
            {
                KeyDown = 1,
                RepeatCount = 3,
                VirtualKeyCode = (ushort)ConsoleKey.C,
                UnicodeCharacter = 'c',
                ControlKeyState = 8,
            },
        }, default).ToArray();

        events.Should().HaveCount(3);
        events.Should().OnlyContain(input => input.Key == new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true));
    }

    [Fact]
    public void Utf16SurrogatePairs_AreOneTextEventPerRepeat()
    {
        WindowsInputNormalizer normalizer = new();
        WindowsInputRecord high = Key('\ud83d', 2);
        WindowsInputRecord low = Key('\ude00', 2);
        normalizer.Decode(high, default).Should().BeEmpty();
        normalizer.Decode(low, default).Select(input => input.Text).Should().Equal("😀", "😀");
    }

    [Fact]
    public void SyntheticModifierRecords_DoNotSplitUtf16SurrogatePairs()
    {
        WindowsInputNormalizer normalizer = new();
        normalizer.Decode(Key('\ud83d'), default).Should().BeEmpty();
        ushort[] virtualKeys = [0x10, 0x11, 0x12, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5];
        foreach (ushort virtualKey in virtualKeys)
        {
            WindowsInputRecord modifier = Key('\0');
            modifier.Key.VirtualKeyCode = virtualKey;
            normalizer.Decode(modifier, default).Should().BeEmpty();
        }

        normalizer.Decode(Key('\ude00'), default).Should().ContainSingle().Which.Text.Should().Be("😀");
    }

    [Fact]
    public void AltGrText_IsNotConvertedToControlC()
    {
        WindowsInputNormalizer normalizer = new();
        WindowsInputRecord input = Key('ç');
        input.Key.VirtualKeyCode = (ushort)ConsoleKey.C;
        input.Key.ControlKeyState = 8 | 1;
        normalizer.Decode(input, default).Should().ContainSingle().Which.Key.KeyChar.Should().Be('ç');
    }

    [Fact]
    public void KeyUp_IsIgnoredExceptAltNumpadCompletion()
    {
        WindowsInputNormalizer normalizer = new();
        WindowsInputRecord input = Key('a');
        input.Key.KeyDown = 0;
        normalizer.Decode(input, default).Should().BeEmpty();
        input.Key.VirtualKeyCode = 0x12;
        input.Key.UnicodeCharacter = 'é';
        normalizer.Decode(input, default).Should().ContainSingle().Which.Key.KeyChar.Should().Be('é');
    }

    [Fact]
    public void Mouse_TracksOnlyLeftButtonAndFocusLoss()
    {
        WindowsInputNormalizer normalizer = new();
        WindowsConsoleWindow window = new(0, 0, 80, 24);
        normalizer.Decode(Mouse(2, 0), window).Should().BeEmpty();
        normalizer.Decode(Mouse(1, 0), window).Should().ContainSingle().Which.Kind.Should().Be(TerminalInputKind.PointerDown);
        normalizer.Decode(Mouse(1, 1), window).Should().ContainSingle().Which.Kind.Should().Be(TerminalInputKind.PointerMove);
        normalizer.Decode(Mouse(0, 0), window).Should().ContainSingle().Which.Kind.Should().Be(TerminalInputKind.PointerUp);
        normalizer.Decode(Mouse(1, 0), window).Should().ContainSingle();
        normalizer.Decode(new WindowsInputRecord { EventType = 16, Focused = 0 }, window)
            .Should().ContainSingle().Which.Kind.Should().Be(TerminalInputKind.FocusLost);
        normalizer.Decode(Mouse(0, 0), window).Should().BeEmpty();
    }

    [Fact]
    public void ReleasingCapture_DoesNotCarryOldDragStateIntoTheNextPress()
    {
        WindowsInputNormalizer normalizer = new();
        normalizer.Decode(Mouse(1, 0), default).Should().ContainSingle().Which.Kind.Should().Be(TerminalInputKind.PointerDown);
        normalizer.ResetPointer();
        normalizer.Decode(Mouse(1, 0), default).Should().ContainSingle().Which.Kind.Should().Be(TerminalInputKind.PointerDown);
    }

    [Fact]
    public void Modes_AreRestoredExactlyAfterCaptureChanges()
    {
        IWindowsTerminalNative native = Substitute.For<IWindowsTerminalNative>();
        const uint originalInput = 0x03f7;
        const uint originalOutput = 0x0001;
        WindowsCursorInfo cursor = new() { Size = 30, Visible = 0 };
        native.GetInputMode().Returns(originalInput);
        native.GetOutputMode().Returns(originalOutput);
        native.GetCursor().Returns(cursor);
        WindowsTerminalDriver driver = new(native);

        driver.Activate();
        native.Received().SetInputMode(Arg.Is<uint>(mode =>
            (mode & (WindowsTerminalDriver.ProcessedInput | WindowsTerminalDriver.QuickEditMode)) == 0
            && (mode & (WindowsTerminalDriver.MouseInput | WindowsTerminalDriver.WindowInput | WindowsTerminalDriver.VirtualTerminalInput)) != 0));
        native.Received().SetOutputMode(originalOutput | 3 | WindowsTerminalDriver.VirtualTerminalOutput | WindowsTerminalDriver.DisableNewlineAutoReturn);

        driver.SetMouseCapture(false);
        native.Received().SetInputMode(Arg.Is<uint>(mode => (mode & WindowsTerminalDriver.ProcessedInput) == 0
            && (mode & WindowsTerminalDriver.MouseInput) == 0 && (mode & WindowsTerminalDriver.QuickEditMode) != 0));

        driver.Restore();
        driver.Restore();
        driver.Dispose();
        native.Received(1).SetInputMode(originalInput);
        native.Received(1).SetOutputMode(originalOutput);
        native.Received(1).SetCursor(cursor);
    }

    [Fact]
    public void PartialActivation_IsRestorableEvenWhenFirstModeWriteFails()
    {
        IWindowsTerminalNative native = Substitute.For<IWindowsTerminalNative>();
        native.GetInputMode().Returns(7U);
        native.GetOutputMode().Returns(1U);
        native.When(instance => instance.SetOutputMode(15U)).Do(_ => throw new Win32Exception(5));
        WindowsTerminalDriver driver = new(native);

        Action activate = driver.Activate;
        activate.Should().Throw<Win32Exception>();
        driver.Restore();
        native.Received().SetInputMode(7);
        native.Received().SetOutputMode(1);
        native.Received().SetCursor(Arg.Any<WindowsCursorInfo>());
    }

    [Fact]
    public void CaptureChangeAfterRestoration_CannotReenableRawInput()
    {
        IWindowsTerminalNative native = Substitute.For<IWindowsTerminalNative>();
        WindowsTerminalDriver driver = new(native);
        driver.Activate();
        driver.Restore();
        native.ClearReceivedCalls();
        Action capture = () => driver.SetMouseCapture(true);
        capture.Should().Throw<InvalidOperationException>();
        native.DidNotReceive().SetInputMode(Arg.Any<uint>());
    }

    [Fact]
    public void BracketedPasteThroughNativeRecords_IsNotReplayedAsShortcuts()
    {
        WindowsInputNormalizer normalizer = new();
        List<TerminalInputEvent> events = [];
        foreach (char character in "\u001b[200~cq\u0003😀\u001b[201~q")
        {
            events.AddRange(normalizer.Decode(Key(character), default));
        }

        events.Should().Equal(new TerminalInputEvent(TerminalInputKind.Paste, Text: "cq\u0003😀"), TerminalKey.Create('q'));
    }

    [Fact]
    public void NativeEscape_IsDeliveredAfterBoundedAmbiguityTimeout()
    {
        WindowsInputNormalizer normalizer = new();
        normalizer.Decode(Key('\u001b'), default).Should().BeEmpty();
        normalizer.ExpireEscape().Should().Equal(TerminalKey.Create('\u001b'));
        normalizer.Decode(Key('q'), default).Should().Equal(TerminalKey.Create('q'));
    }

    [Fact]
    public void NativeMouseAfterEscape_DoesNotPostponeEscapeUntilTheMouseStopsMoving()
    {
        WindowsInputNormalizer normalizer = new();
        normalizer.Decode(Key('\u001b'), default).Should().BeEmpty();
        normalizer.Decode(Mouse(1, 0), default).Should().Equal(
            TerminalKey.Create('\u001b'), new TerminalInputEvent(TerminalInputKind.PointerDown));
    }

    [Fact]
    public void FailedInputRestoration_DoesNotSkipOutputOrCursor()
    {
        IWindowsTerminalNative native = Substitute.For<IWindowsTerminalNative>();
        native.GetInputMode().Returns(7U);
        native.GetOutputMode().Returns(1U);
        WindowsTerminalDriver driver = new(native);
        driver.Activate();
        native.When(instance => instance.SetInputMode(7U)).Do(_ => throw new Win32Exception(5));

        Action restore = driver.Restore;
        restore.Should().Throw<Win32Exception>();
        native.Received().SetOutputMode(1);
        native.Received().SetCursor(Arg.Any<WindowsCursorInfo>());
    }

    private static WindowsInputRecord Key(char character, ushort repeat = 1)
    {
        return new WindowsInputRecord
        {
            EventType = 1,
            Key = new WindowsKeyRecord { KeyDown = 1, RepeatCount = repeat, UnicodeCharacter = character },
        };
    }

    private static WindowsInputRecord Wheel(short delta)
    {
        WindowsInputRecord record = Mouse(unchecked((uint)(ushort)delta << 16), 4);
        record.Mouse.Position = new WindowsCoordinate { X = 12, Y = 23 };
        return record;
    }

    private static WindowsInputRecord Mouse(uint buttons, uint flags)
    {
        return new WindowsInputRecord
        {
            EventType = 2,
            Mouse = new WindowsMouseRecord { ButtonState = buttons, EventFlags = flags },
        };
    }
}
