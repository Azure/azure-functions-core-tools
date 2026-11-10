// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Console.Terminal;

internal static class TerminalKey
{
    public static bool IsInterrupt(char character, ConsoleKey key, ConsoleModifiers modifiers)
        => character == '\u0003'
            || (key == ConsoleKey.C && (modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == ConsoleModifiers.Control);

    public static TerminalInputEvent Create(char character, ConsoleKey key = default, ConsoleModifiers modifiers = default)
    {
        if (character == '\u0003')
        {
            key = ConsoleKey.C;
            modifiers |= ConsoleModifiers.Control;
        }
        else if (key == default)
        {
            key = character switch
            {
                '\r' or '\n' => ConsoleKey.Enter,
                '\t' => ConsoleKey.Tab,
                '\b' or '\u007f' => ConsoleKey.Backspace,
                '\u001b' => ConsoleKey.Escape,
                ' ' => ConsoleKey.Spacebar,
                >= 'a' and <= 'z' => ConsoleKey.A + (character - 'a'),
                >= 'A' and <= 'Z' => ConsoleKey.A + (character - 'A'),
                >= '0' and <= '9' => ConsoleKey.D0 + (character - '0'),
                >= '\u0001' and <= '\u001a' => ConsoleKey.A + (character - 1),
                _ => default,
            };

            if (character is >= '\u0001' and <= '\u001a' and not ('\b' or '\t' or '\r' or '\n'))
            {
                modifiers |= ConsoleModifiers.Control;
            }

            if (character is >= 'A' and <= 'Z')
            {
                modifiers |= ConsoleModifiers.Shift;
            }
        }

        return new TerminalInputEvent(TerminalInputKind.Key, new ConsoleKeyInfo(
            character, key,
            (modifiers & ConsoleModifiers.Shift) != 0,
            (modifiers & ConsoleModifiers.Alt) != 0,
            (modifiers & ConsoleModifiers.Control) != 0));
    }
}
