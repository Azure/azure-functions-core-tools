// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;

namespace Azure.Functions.Cli.Console.Terminal;

internal sealed class WindowsInputNormalizer
{
    private int _wheelRemainder;
    private bool _leftDown;
    private char? _highSurrogate;
    private ushort _surrogateRepeat;
    private int _resetPointer;
    private readonly VtInputDecoder _protocol = new();

    public void ResetPointer() => Interlocked.Exchange(ref _resetPointer, 1);

    public IReadOnlyList<TerminalInputEvent> ExpireEscape() => _protocol.ExpireEscape();

    public IEnumerable<TerminalInputEvent> DecodeManagedKey(ConsoleKeyInfo key)
    {
        WindowsKeyRecord record = new()
        {
            KeyDown = 1,
            RepeatCount = 1,
            VirtualKeyCode = (ushort)key.Key,
            UnicodeCharacter = key.KeyChar,
            ControlKeyState = ((key.Modifiers & ConsoleModifiers.Shift) != 0 ? 0x10U : 0)
                | ((key.Modifiers & ConsoleModifiers.Alt) != 0 ? 0x02U : 0)
                | ((key.Modifiers & ConsoleModifiers.Control) != 0 ? 0x08U : 0),
        };

        // Managed input has already decoded keys and escape sequences. Share only UTF-16 assembly,
        // so an Escape followed by another managed key is not parsed again as a VT prefix.
        return DecodeKey(record);
    }

    public IEnumerable<TerminalInputEvent> Decode(WindowsInputRecord record, WindowsConsoleWindow window)
    {
        if (Interlocked.Exchange(ref _resetPointer, 0) != 0)
        {
            _leftDown = false;
            _wheelRemainder = 0;
        }

        if (record.EventType != 1 && _protocol.IsEscapePending)
        {
            foreach (TerminalInputEvent escape in _protocol.Complete())
            {
                yield return escape;
            }
        }

        switch (record.EventType)
        {
            case 1:
                foreach (TerminalInputEvent input in DecodeKey(record.Key))
                {
                    foreach (TerminalInputEvent decoded in DecodeProtocol(input))
                    {
                        yield return decoded;
                    }
                }

                break;
            case 2:
                foreach (TerminalInputEvent input in DecodeMouse(record.Mouse, window))
                {
                    yield return input;
                }

                break;
            case 4:
                yield return new TerminalInputEvent(TerminalInputKind.Resize);
                break;
            case 16 when record.Focused == 0:
                _leftDown = false;
                yield return new TerminalInputEvent(TerminalInputKind.FocusLost);
                break;
        }
    }

    private IReadOnlyList<TerminalInputEvent> DecodeProtocol(TerminalInputEvent input)
    {
        if (!_protocol.HasPendingProtocol && input.Key.KeyChar != '\u001b')
        {
            return [input];
        }

        if (input.Kind == TerminalInputKind.Key && input.Key.KeyChar == '\0')
        {
            if (_protocol.IsEscapePending)
            {
                return [.. _protocol.Complete(), input];
            }

            // ConPTY can insert synthetic modifier/numpad records while delivering Unicode paste.
            return [];
        }

        // VT input preserves bracketed-paste framing that ConPTY otherwise strips before ReadConsoleInputW.
        // Native key and mouse records are still supported; only protocol-prefixed characters enter this decoder.
        string text = input.Text ?? input.Key.KeyChar.ToString();
        return _protocol.Feed(Encoding.UTF8.GetBytes(text));
    }

    private IEnumerable<TerminalInputEvent> DecodeKey(WindowsKeyRecord key)
    {
        // Alt-numpad input is delivered on the final Alt key-up record.
        if (key.KeyDown == 0 && (key.VirtualKeyCode != 0x12 || key.UnicodeCharacter == 0))
        {
            yield break;
        }

        char character = (char)key.UnicodeCharacter;
        int repetitions = Math.Max(1, (int)key.RepeatCount);
        ConsoleModifiers modifiers = ((key.ControlKeyState & 0x10) != 0 ? ConsoleModifiers.Shift : 0)
            | ((key.ControlKeyState & 0x03) != 0 ? ConsoleModifiers.Alt : 0)
            | ((key.ControlKeyState & 0x0c) != 0 ? ConsoleModifiers.Control : 0);

        if (key.VirtualKeyCode == (ushort)ConsoleKey.C
            && (modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == ConsoleModifiers.Control)
        {
            character = '\u0003';
        }

        // ConPTY can synthesize modifier transitions between the two UTF-16 halves.
        // They are not text and must not flush the pending high surrogate.
        if (character == 0 && key.VirtualKeyCode is 0 or 0x10 or 0x11 or 0x12 or >= 0xa0 and <= 0xa5)
        {
            yield break;
        }

        if (_highSurrogate.HasValue && character != 0)
        {
            char high = _highSurrogate.Value;
            _highSurrogate = null;
            if (char.IsLowSurrogate(character))
            {
                string text = string.Concat(high, character);
                for (int i = 0; i < Math.Max(repetitions, _surrogateRepeat); i++)
                {
                    yield return new TerminalInputEvent(TerminalInputKind.Text, Text: text);
                }

                yield break;
            }

            for (int i = 0; i < _surrogateRepeat; i++)
            {
                yield return new TerminalInputEvent(TerminalInputKind.Text, Text: "\ufffd");
            }
        }

        if (char.IsHighSurrogate(character))
        {
            _highSurrogate = character;
            _surrogateRepeat = (ushort)repetitions;
            yield break;
        }

        for (int i = 0; i < repetitions; i++)
        {
            if (char.IsLowSurrogate(character))
            {
                yield return new TerminalInputEvent(TerminalInputKind.Text, Text: "\ufffd");
            }
            else
            {
                yield return TerminalKey.Create(character, (ConsoleKey)key.VirtualKeyCode, modifiers);
            }
        }
    }

    private IEnumerable<TerminalInputEvent> DecodeMouse(WindowsMouseRecord mouse, WindowsConsoleWindow window)
    {
        int x = mouse.Position.X - window.Left;
        int y = mouse.Position.Y - window.Top;
        if ((mouse.EventFlags & 4) != 0)
        {
            _wheelRemainder += (short)(mouse.ButtonState >> 16) * 3;
            int rows = _wheelRemainder / 120;
            _wheelRemainder %= 120;
            if (rows != 0)
            {
                yield return new TerminalInputEvent(TerminalInputKind.Wheel, X: x, Y: y, Delta: rows);
            }

            yield break;
        }

        // Horizontal wheel records are not vertical scroll or button transitions.
        if ((mouse.EventFlags & 8) != 0)
        {
            yield break;
        }

        bool down = (mouse.ButtonState & 1) != 0;
        if (down != _leftDown)
        {
            _leftDown = down;
            yield return new TerminalInputEvent(down ? TerminalInputKind.PointerDown : TerminalInputKind.PointerUp, X: x, Y: y);
        }
        else if (down && (mouse.EventFlags & 1) != 0)
        {
            yield return new TerminalInputEvent(TerminalInputKind.PointerMove, X: x, Y: y);
        }
    }
}
