// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers;
using System.Globalization;
using System.Text;

namespace Azure.Functions.Cli.Console.Terminal;

/// <summary>
/// Incrementally consumes terminal input without replaying unsupported protocol bytes as shortcuts.
/// </summary>
internal sealed class VtInputDecoder
{
    internal const int MaximumSequenceBytes = 64;
    internal const int MaximumPasteBytes = 65_536;
    private const int MaximumCoordinate = 32_767;
    private const string PasteTerminator = "\u001b[201~";
    private readonly byte[] _sequence = new byte[MaximumSequenceBytes];
    private readonly byte[] _utf8 = new byte[4];
    private readonly List<byte> _paste = [];
    private ParserState _state;
    private int _sequenceLength;
    private int _utf8Length;
    private int _pasteTerminatorLength;
    private int _legacyValues;
    private int _legacyContinuation;
    private bool _pasteTooLarge;
    private bool _leftDown;
    private bool _altText;
    private bool _stringAcceptsBell;

    internal bool HasPendingProtocol => _state != ParserState.Ground;

    internal bool IsEscapePending => _state is ParserState.Escape or ParserState.ExpiredEscape;

    public IReadOnlyList<TerminalInputEvent> Feed(ReadOnlySpan<byte> bytes)
    {
        List<TerminalInputEvent> events = [];
        foreach (byte value in bytes)
        {
            Consume(value, events);
        }

        return events;
    }

    /// <summary>
    /// Resolves a lone Escape after an input-poll timeout; partial protocols and UTF-8 remain buffered.
    /// </summary>
    public IReadOnlyList<TerminalInputEvent> ExpireEscape()
    {
        if (_state != ParserState.Escape)
        {
            return [];
        }

        _state = ParserState.ExpiredEscape;
        return [TerminalKey.Create('\u001b')];
    }

    public IReadOnlyList<TerminalInputEvent> Complete()
    {
        List<TerminalInputEvent> events = [];
        if (_state == ParserState.Paste)
        {
            events.Add(new TerminalInputEvent(TerminalInputKind.Paste));
        }
        else if (_state == ParserState.Escape)
        {
            events.Add(TerminalKey.Create('\u001b'));
        }
        else if (_utf8Length != 0)
        {
            events.Add(new TerminalInputEvent(TerminalInputKind.Text, Text: "\ufffd"));
        }

        _state = ParserState.Ground;
        _utf8Length = 0;
        _paste.Clear();
        return events;
    }

    private void Consume(byte value, List<TerminalInputEvent> events)
    {
        switch (_state)
        {
            case ParserState.Ground:
                ConsumeGround(value, events);
                break;
            case ParserState.Escape:
                ConsumeEscape(value, events);
                break;
            case ParserState.ExpiredEscape:
                // A delayed protocol prefix must not become shortcuts after Escape's ambiguity timeout.
                if (value is (byte)'[' or (byte)'O' or (byte)']' or (byte)'P' or (byte)'X' or (byte)'^' or (byte)'_')
                {
                    ConsumeEscape(value, events);
                }
                else
                {
                    _state = ParserState.Ground;
                    ConsumeGround(value, events);
                }

                break;
            case ParserState.Csi:
            case ParserState.Ss3:
                ConsumeSequence(value, events);
                break;
            case ParserState.DiscardSequence:
                if (value is >= 0x40 and <= 0x7e)
                {
                    _state = ParserState.Ground;
                }

                break;
            case ParserState.EscapeIntermediate:
                if (value is >= 0x30 and <= 0x7e)
                {
                    _state = ParserState.Ground;
                }

                break;
            case ParserState.ControlString:
                if (value == 0x07 && _stringAcceptsBell)
                {
                    _state = ParserState.Ground;
                }
                else if (value == 0x1b)
                {
                    _state = ParserState.ControlStringEscape;
                }

                break;
            case ParserState.ControlStringEscape:
                _state = value == (byte)'\\' ? ParserState.Ground
                    : value == 0x1b ? ParserState.ControlStringEscape : ParserState.ControlString;
                break;
            case ParserState.LegacyMouse:
                ConsumeLegacyMouse(value);
                break;
            case ParserState.Paste:
                ConsumePaste(value, events);
                break;
        }
    }

    private void ConsumeGround(byte value, List<TerminalInputEvent> events)
    {
        if (_utf8Length != 0)
        {
            if ((value & 0xc0) != 0x80)
            {
                _utf8Length = 0;
                events.Add(new TerminalInputEvent(TerminalInputKind.Text, Text: "\ufffd"));
                ConsumeGround(value, events);
                return;
            }

            _utf8[_utf8Length++] = value;
            OperationStatus status = Rune.DecodeFromUtf8(_utf8.AsSpan(0, _utf8Length), out Rune rune, out _);
            if (status != OperationStatus.NeedMoreData || _utf8Length == _utf8.Length)
            {
                events.Add(new TerminalInputEvent(
                    TerminalInputKind.Text,
                    Key: new ConsoleKeyInfo('\0', default, false, _altText, false),
                    Text: status == OperationStatus.Done ? rune.ToString() : "\ufffd"));
                _utf8Length = 0;
                _altText = false;
            }

            return;
        }

        if (value == 0x1b)
        {
            _state = ParserState.Escape;
        }
        else if (value == 0x9b)
        {
            BeginSequence(ParserState.Csi);
        }
        else if (value is 0x90 or 0x9d or 0x9e or 0x9f)
        {
            _stringAcceptsBell = value == 0x9d;
            _state = ParserState.ControlString;
        }
        else if (value < 0x80)
        {
            events.Add(TerminalKey.Create((char)value));
        }
        else if (value is >= 0xc2 and <= 0xf4)
        {
            _utf8[0] = value;
            _utf8Length = 1;
        }
        else
        {
            events.Add(new TerminalInputEvent(TerminalInputKind.Text, Text: "\ufffd"));
        }
    }

    private void ConsumeEscape(byte value, List<TerminalInputEvent> events)
    {
        switch (value)
        {
            case (byte)'[':
                BeginSequence(ParserState.Csi);
                break;
            case (byte)'O':
                BeginSequence(ParserState.Ss3);
                break;
            case (byte)']':
                _stringAcceptsBell = true;
                _state = ParserState.ControlString;
                break;
            case (byte)'P':
            case (byte)'X':
            case (byte)'^':
            case (byte)'_':
                _stringAcceptsBell = false;
                _state = ParserState.ControlString;
                break;
            case 0x1b:
                events.Add(TerminalKey.Create('\u001b'));
                break;
            case >= 0x20 and <= 0x2f:
                _state = ParserState.EscapeIntermediate;
                break;
            default:
                _state = ParserState.Ground;
                if (value < 0x80)
                {
                    events.Add(TerminalKey.Create((char)value, modifiers: ConsoleModifiers.Alt));
                }
                else
                {
                    _altText = true;
                    ConsumeGround(value, events);
                }

                break;
        }
    }

    private void BeginSequence(ParserState state)
    {
        _state = state;
        _sequenceLength = 0;
    }

    private void ConsumeSequence(byte value, List<TerminalInputEvent> events)
    {
        if (value is >= 0x40 and <= 0x7e)
        {
            bool csi = _state == ParserState.Csi;
            _state = ParserState.Ground;
            ParseSequence((char)value, csi, events);
        }
        else if (value is < 0x20 or > 0x3f || _sequenceLength == MaximumSequenceBytes)
        {
            _state = ParserState.DiscardSequence;
        }
        else
        {
            _sequence[_sequenceLength++] = value;
        }
    }

    private void ParseSequence(char final, bool csi, List<TerminalInputEvent> events)
    {
        ReadOnlySpan<byte> bytes = _sequence.AsSpan(0, _sequenceLength);
        if (csi && final == 'M' && bytes.IsEmpty)
        {
            _state = ParserState.LegacyMouse;
            _legacyValues = 3;
            _legacyContinuation = 0;
            return;
        }

        if (csi && !bytes.IsEmpty && bytes[0] == '<')
        {
            ParseMouse(bytes[1..], final, events);
            return;
        }

        if (csi && bytes.IsEmpty && final is 'I' or 'O')
        {
            if (final == 'O')
            {
                _leftDown = false;
                events.Add(new TerminalInputEvent(TerminalInputKind.FocusLost));
            }

            return;
        }

        Span<int> parameters = stackalloc int[3];
        if (!TryParseParameters(bytes, parameters, out int count))
        {
            return;
        }

        if (csi && final == '~' && count == 1 && parameters[0] == 200)
        {
            _state = ParserState.Paste;
            _paste.Clear();
            _pasteTooLarge = false;
            _pasteTerminatorLength = 0;
            return;
        }

        ConsoleModifiers modifiers = default;
        if (count > 1)
        {
            if (count != 2 || parameters[1] is < 1 or > 8)
            {
                return;
            }

            int bits = parameters[1] - 1;
            modifiers = ((bits & 1) != 0 ? ConsoleModifiers.Shift : 0)
                | ((bits & 2) != 0 ? ConsoleModifiers.Alt : 0)
                | ((bits & 4) != 0 ? ConsoleModifiers.Control : 0);
        }

        if (final != '~' && count != 0 && parameters[0] != 1)
        {
            return;
        }

        ConsoleKey key = final switch
        {
            'A' => ConsoleKey.UpArrow,
            'B' => ConsoleKey.DownArrow,
            'C' => ConsoleKey.RightArrow,
            'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home,
            'F' => ConsoleKey.End,
            'P' => ConsoleKey.F1,
            'Q' => ConsoleKey.F2,
            'R' when !csi => ConsoleKey.F3,
            'S' => ConsoleKey.F4,
            'Z' when csi => ConsoleKey.Tab,
            '~' when csi && count > 0 => parameters[0] switch
            {
                1 or 7 => ConsoleKey.Home,
                2 => ConsoleKey.Insert,
                3 => ConsoleKey.Delete,
                4 or 8 => ConsoleKey.End,
                5 => ConsoleKey.PageUp,
                6 => ConsoleKey.PageDown,
                11 => ConsoleKey.F1,
                12 => ConsoleKey.F2,
                13 => ConsoleKey.F3,
                14 => ConsoleKey.F4,
                15 => ConsoleKey.F5,
                17 => ConsoleKey.F6,
                18 => ConsoleKey.F7,
                19 => ConsoleKey.F8,
                20 => ConsoleKey.F9,
                21 => ConsoleKey.F10,
                23 => ConsoleKey.F11,
                24 => ConsoleKey.F12,
                _ => default,
            },
            _ => default,
        };

        if (key != default)
        {
            if (final == 'Z')
            {
                modifiers |= ConsoleModifiers.Shift;
            }

            events.Add(TerminalKey.Create(key == ConsoleKey.Tab ? '\t' : '\0', key, modifiers));
        }
    }

    private void ParseMouse(ReadOnlySpan<byte> bytes, char final, List<TerminalInputEvent> events)
    {
        Span<int> values = stackalloc int[3];
        if (final is not ('M' or 'm') || !TryParseParameters(bytes, values, out int count)
            || count != 3 || values[0] > 127
            || values[1] is < 1 or > MaximumCoordinate || values[2] is < 1 or > MaximumCoordinate)
        {
            return;
        }

        int button = values[0];
        int x = values[1] - 1;
        int y = values[2] - 1;
        int which = button & 3;
        if ((button & 64) != 0)
        {
            if (final == 'M' && which is 0 or 1)
            {
                events.Add(new TerminalInputEvent(TerminalInputKind.Wheel, X: x, Y: y, Delta: which == 0 ? 3 : -3));
            }

            return;
        }

        if (which != 0)
        {
            return;
        }

        if (final == 'm')
        {
            if (_leftDown)
            {
                events.Add(new TerminalInputEvent(TerminalInputKind.PointerUp, X: x, Y: y));
            }

            _leftDown = false;
        }
        else if ((button & 32) != 0)
        {
            if (_leftDown)
            {
                events.Add(new TerminalInputEvent(TerminalInputKind.PointerMove, X: x, Y: y));
            }
        }
        else
        {
            _leftDown = true;
            events.Add(new TerminalInputEvent(TerminalInputKind.PointerDown, X: x, Y: y));
        }
    }

    private void ConsumeLegacyMouse(byte value)
    {
        if (_legacyContinuation != 0 && (value & 0xc0) == 0x80)
        {
            _legacyContinuation--;
            if (_legacyContinuation != 0)
            {
                return;
            }
        }
        else if (value is >= 0xc2 and <= 0xf4)
        {
            _legacyContinuation = value < 0xe0 ? 1 : value < 0xf0 ? 2 : 3;
            return;
        }
        else
        {
            _legacyContinuation = 0;
        }

        if (--_legacyValues == 0)
        {
            _state = ParserState.Ground;
        }
    }

    private void ConsumePaste(byte value, List<TerminalInputEvent> events)
    {
        if (value == PasteTerminator[_pasteTerminatorLength])
        {
            if (++_pasteTerminatorLength == PasteTerminator.Length)
            {
                // Null explicitly represents rejected paste; no fragment is ever delivered as commands.
                string? text = _pasteTooLarge ? null : Encoding.UTF8.GetString(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_paste));
                events.Add(new TerminalInputEvent(TerminalInputKind.Paste, Text: text));
                _paste.Clear();
                _pasteTerminatorLength = 0;
                _state = ParserState.Ground;
            }

            return;
        }

        for (int i = 0; i < _pasteTerminatorLength; i++)
        {
            AddPasteByte((byte)PasteTerminator[i]);
        }

        _pasteTerminatorLength = 0;
        if (value == 0x1b)
        {
            _pasteTerminatorLength = 1;
        }
        else
        {
            AddPasteByte(value);
        }
    }

    private void AddPasteByte(byte value)
    {
        if (_pasteTooLarge)
        {
            return;
        }

        if (_paste.Count == MaximumPasteBytes)
        {
            _paste.Clear();
            _pasteTooLarge = true;
            return;
        }

        _paste.Add(value);
    }

    private static bool TryParseParameters(ReadOnlySpan<byte> bytes, Span<int> values, out int count)
    {
        count = 0;
        if (bytes.IsEmpty)
        {
            return true;
        }

        while (!bytes.IsEmpty && count < values.Length)
        {
            int separator = bytes.IndexOf((byte)';');
            ReadOnlySpan<byte> part = separator < 0 ? bytes : bytes[..separator];
            if (part.IsEmpty || part.Length > 6
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out values[count++]))
            {
                return false;
            }

            if (separator < 0)
            {
                return true;
            }

            bytes = bytes[(separator + 1)..];
            if (bytes.IsEmpty)
            {
                return false;
            }
        }

        return false;
    }

    private enum ParserState
    {
        Ground,
        Escape,
        ExpiredEscape,
        Csi,
        Ss3,
        DiscardSequence,
        EscapeIntermediate,
        ControlString,
        ControlStringEscape,
        LegacyMouse,
        Paste,
    }
}
