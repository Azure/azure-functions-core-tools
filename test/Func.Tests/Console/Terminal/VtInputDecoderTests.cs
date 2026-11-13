// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using Azure.Functions.Cli.Console.Terminal;

namespace Azure.Functions.Cli.Tests.Console.Terminal;

public class VtInputDecoderTests
{
    public static IEnumerable<object[]> Sequences()
    {
        yield return ["\u001b[A", TerminalKey.Create('\0', ConsoleKey.UpArrow)];
        yield return ["\u001bOB", TerminalKey.Create('\0', ConsoleKey.DownArrow)];
        yield return ["\u001b[1;5C", TerminalKey.Create('\0', ConsoleKey.RightArrow, ConsoleModifiers.Control)];
        yield return ["\u001b[1;3D", TerminalKey.Create('\0', ConsoleKey.LeftArrow, ConsoleModifiers.Alt)];
        yield return ["\u001b[5~", TerminalKey.Create('\0', ConsoleKey.PageUp)];
        yield return ["\u001b[6;2~", TerminalKey.Create('\0', ConsoleKey.PageDown, ConsoleModifiers.Shift)];
        yield return ["\u001b[H", TerminalKey.Create('\0', ConsoleKey.Home)];
        yield return ["\u001bOF", TerminalKey.Create('\0', ConsoleKey.End)];
        yield return ["\u001b[Z", TerminalKey.Create('\t', ConsoleKey.Tab, ConsoleModifiers.Shift)];
        yield return ["\u001bOP", TerminalKey.Create('\0', ConsoleKey.F1)];
        yield return ["\u001b[24~", TerminalKey.Create('\0', ConsoleKey.F12)];
        yield return ["\u0003", TerminalKey.Create('\u0003')];
        yield return ["\u001bq", TerminalKey.Create('q', modifiers: ConsoleModifiers.Alt)];
        yield return ["\u001b[<64;5;3M", new TerminalInputEvent(TerminalInputKind.Wheel, X: 4, Y: 2, Delta: 3)];
        yield return ["\u001b[<81;5;3M", new TerminalInputEvent(TerminalInputKind.Wheel, X: 4, Y: 2, Delta: -3)];
        yield return ["\u001b[<0;1;1M", new TerminalInputEvent(TerminalInputKind.PointerDown)];
        yield return ["\u001b[O", new TerminalInputEvent(TerminalInputKind.FocusLost)];
        yield return ["é", new TerminalInputEvent(TerminalInputKind.Text, Text: "é")];
        yield return ["界", new TerminalInputEvent(TerminalInputKind.Text, Text: "界")];
        yield return ["😀", new TerminalInputEvent(TerminalInputKind.Text, Text: "😀")];
        yield return ["\u001b[200~q\u0003界\nc\u001b[201~", new TerminalInputEvent(TerminalInputKind.Paste, Text: "q\u0003界\nc")];
    }

    [Theory]
    [MemberData(nameof(Sequences))]
    public void SupportedSequences_SurviveEveryByteBoundary(string sequence, object expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);
        for (int split = 0; split <= bytes.Length; split++)
        {
            VtInputDecoder decoder = new();
            List<TerminalInputEvent> events = [];
            events.AddRange(decoder.Feed(bytes.AsSpan(0, split)));
            events.AddRange(decoder.Feed(bytes.AsSpan(split)));
            events.Should().ContainSingle().Which.Should().Be(expected, $"split {split} must preserve the input");
        }

        VtInputDecoder bytewise = new();
        List<TerminalInputEvent> individual = [];
        foreach (byte value in bytes)
        {
            individual.AddRange(bytewise.Feed([value]));
        }

        individual.Should().ContainSingle().Which.Should().Be(expected);
    }

    [Fact]
    public void Mouse_OnlyLeftTransitionsAndDragsAreDelivered()
    {
        VtInputDecoder decoder = new();
        var events = decoder.Feed(Encoding.UTF8.GetBytes(
            "\u001b[<2;3;4M\u001b[<2;3;4m\u001b[<32;3;4M"
            + "\u001b[<4;3;4M\u001b[<48;5;6M\u001b[<0;6;7m"
            + "\u001b[<32;8;9M\u001b[<66;8;9M"));

        events.Should().Equal(
            new TerminalInputEvent(TerminalInputKind.PointerDown, X: 2, Y: 3),
            new TerminalInputEvent(TerminalInputKind.PointerMove, X: 4, Y: 5),
            new TerminalInputEvent(TerminalInputKind.PointerUp, X: 5, Y: 6));
    }

    [Fact]
    public void FocusLoss_CancelsDragging()
    {
        VtInputDecoder decoder = new();
        var events = decoder.Feed(Encoding.UTF8.GetBytes("\u001b[<0;1;1M\u001b[O\u001b[<32;2;2M\u001b[<0;2;2m"));
        events.Select(input => input.Kind).Should().Equal(TerminalInputKind.PointerDown, TerminalInputKind.FocusLost);
    }

    [Theory]
    [InlineData("\u001b[<64;0;1M")]
    [InlineData("\u001b[<64;32768;1M")]
    [InlineData("\u001b[<64;999999999999999999999999999;1M")]
    [InlineData("\u001b[<256;1;1M")]
    [InlineData("\u001b[<64;;1M")]
    [InlineData("\u001b[<64;1;1;1M")]
    [InlineData("\u001b[999999;9~")]
    [InlineData("\u001b[?25;1$q")]
    [InlineData("\u001b[35;113;99M")]
    [InlineData("\u001b[M qc")]
    [InlineData("\u001b[M éq")]
    [InlineData("\u001b]52;c;cq\u0007")]
    [InlineData("\u001bPqc\u001b\\")]
    [InlineData("\u001bP\u0007qc\u001b\\")]
    [InlineData("\u001bPqc\u001b\u001b\\")]
    [InlineData("\u001b_qc\u001b\\")]
    public void UnsupportedProtocols_AreConsumedWithoutShortcuts(string unsupported)
    {
        VtInputDecoder decoder = new();
        var events = decoder.Feed(Encoding.UTF8.GetBytes(unsupported + "z"));
        events.Should().ContainSingle().Which.Should().Be(TerminalKey.Create('z'));
    }

    [Fact]
    public void OversizedSequence_IsDrainedWithBoundedState()
    {
        VtInputDecoder decoder = new();
        decoder.Feed(Encoding.UTF8.GetBytes("\u001b[<" + new string('1', 100_000))).Should().BeEmpty();
        decoder.Feed("Mq"u8).Should().Equal(TerminalKey.Create('q'));
    }

    [Fact]
    public void EscapeTimeout_DoesNotReleasePartialMouseOrUnicode()
    {
        VtInputDecoder decoder = new();
        decoder.Feed("\u001b"u8).Should().BeEmpty();
        decoder.ExpireEscape().Should().Equal(TerminalKey.Create('\u001b'));
        decoder.ExpireEscape().Should().BeEmpty();

        decoder.Feed("\u001b[<64;1;"u8).Should().BeEmpty();
        decoder.ExpireEscape().Should().BeEmpty();
        decoder.Feed("1M"u8).Should().ContainSingle().Which.Kind.Should().Be(TerminalInputKind.Wheel);

        decoder.Feed([0xe7]).Should().BeEmpty();
        decoder.ExpireEscape().Should().BeEmpty();
        decoder.Feed([0x95, 0x8c]).Should().ContainSingle().Which.Text.Should().Be("界");
    }

    [Fact]
    public void DelayedMousePrefix_AfterEscapeTimeoutCannotBecomeShortcuts()
    {
        VtInputDecoder decoder = new();
        decoder.Feed("\u001b"u8).Should().BeEmpty();
        decoder.ExpireEscape().Should().Equal(TerminalKey.Create('\u001b'));
        decoder.Feed("[<64;5;3Mq"u8).Should().Equal(
            new TerminalInputEvent(TerminalInputKind.Wheel, X: 4, Y: 2, Delta: 3), TerminalKey.Create('q'));
    }

    [Fact]
    public void PasteLimit_RejectsWholePasteWithoutReplayingCommands()
    {
        VtInputDecoder decoder = new();
        decoder.Feed("\u001b[200~"u8).Should().BeEmpty();
        decoder.Feed(Encoding.UTF8.GetBytes(new string('q', VtInputDecoder.MaximumPasteBytes))).Should().BeEmpty();
        decoder.Feed("c"u8).Should().BeEmpty();
        var events = decoder.Feed("\u001b[201~z"u8);
        events.Should().Equal(new TerminalInputEvent(TerminalInputKind.Paste), TerminalKey.Create('z'));
    }

    [Fact]
    public void PasteExactlyAtLimit_IsNotTruncated()
    {
        VtInputDecoder decoder = new();
        string text = new('q', VtInputDecoder.MaximumPasteBytes);
        var events = decoder.Feed(Encoding.UTF8.GetBytes("\u001b[200~" + text + "\u001b[201~"));
        events.Should().ContainSingle().Which.Text.Should().Be(text);
    }

    [Fact]
    public void PasteFalseTerminator_IsKeptAsText()
    {
        VtInputDecoder decoder = new();
        var events = decoder.Feed("\u001b[200~one\u001b[201x\u001b\u001b[201~"u8);
        events.Should().ContainSingle().Which.Text.Should().Be("one\u001b[201x\u001b");
    }

    [Fact]
    public void EndOfInput_ExplicitlyRejectsIncompletePasteAndReplacesIncompleteUnicode()
    {
        VtInputDecoder decoder = new();
        decoder.Feed("\u001b[200~q"u8);
        decoder.Complete().Should().Equal(new TerminalInputEvent(TerminalInputKind.Paste));
        decoder.Feed([0xf0, 0x9f]);
        decoder.Complete().Should().Equal(new TerminalInputEvent(TerminalInputKind.Text, Text: "\ufffd"));
    }

    [Fact]
    public void CombinedInput_PreservesOrderingAndControlC()
    {
        VtInputDecoder decoder = new();
        var events = decoder.Feed("q\u001b[<65;10;20M\u0003c"u8);
        events.Should().HaveCount(4);
        events[0].Key.KeyChar.Should().Be('q');
        events[1].Delta.Should().Be(-3);
        events[2].Key.Should().Be(new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true));
        events[3].Key.KeyChar.Should().Be('c');
    }
}
