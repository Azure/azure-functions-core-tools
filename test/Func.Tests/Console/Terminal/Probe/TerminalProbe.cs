// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

#if TERMINAL_NATIVE_PROBE
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Console.Terminal;
using Microsoft.Win32.SafeHandles;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.TerminalProbe;

internal sealed partial class TerminalProbe
{
    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            return await UnixTerminalProbe.RunAsync(args);
        }

        if (!IsIsolatedConsole())
        {
            System.Console.Error.WriteLine("The native probe was not attached to its own console.");
            return 12;
        }

        // A redirected test host can pass standard handles through despite ConPTY attachment.
        // Reopen them only after proving that this console contains the owned probe process alone.
        using SafeFileHandle consoleInput = OpenConsole("CONIN$");
        using SafeFileHandle consoleOutput = OpenConsole("CONOUT$");
        SetStdHandle(-10, consoleInput);
        SetStdHandle(-11, consoleOutput);
        SetStdHandle(-12, consoleOutput);
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            Interactive = InteractionSupport.Yes,
        });
        TerminalSessionFactory factory = new(new WindowsPlatform());
        System.Console.Write("HANDOFF_READY\r\n");
        ConsoleKeyInfo? promptKey = await console.Input.ReadKeyAsync(true, deadline.Token);
        if (promptKey?.KeyChar != 'a')
        {
            return 10;
        }

        while (!console.Input.IsKeyAvailable())
        {
            await Task.Delay(5, deadline.Token);
        }

        bool handedOff = factory.DrainManagedInput(console, deadline.Token).Any(input => input.Key.KeyChar == 'b');
        WindowsTerminalNative native = new();
        uint inputMode = native.GetInputMode();
        uint outputMode = native.GetOutputMode();
        WindowsCursorInfo cursor = native.GetCursor();
        bool wheel = false;
        bool down = false;
        bool move = false;
        bool up = false;
        bool controlC = false;
        bool unicode = false;
        bool paste = false;
        bool frameFits;
        List<string> observed = [];
        await using (ITerminalSession terminal = factory.Open(console))
        {
            TerminalSize size = terminal.Size;
            string frame = string.Join("\n", Enumerable.Range(0, size.Height).Select(row => new string((char)('A' + row), size.Width)));
            terminal.Render(new Text(frame));
            frameFits = ReadCell(0, 0) == 'A' && ReadCell(size.Width - 1, size.Height - 1) == 'A' + size.Height - 1;
            terminal.WriteControl("\r\nINPUT_READY\r\n");
            await foreach (TerminalInputEvent input in terminal.ReadAsync(deadline.Token))
            {
                observed.Add($"{input.Kind}:{(int)input.Key.KeyChar:X4}:{string.Join(',', (input.Text ?? string.Empty).Select(character => ((int)character).ToString("X4")))}");
                wheel |= input.Kind == TerminalInputKind.Wheel && input.Delta == 3 && input.X == 4 && input.Y == 2;
                down |= input.Kind == TerminalInputKind.PointerDown && input.X == 4 && input.Y == 2;
                move |= input.Kind == TerminalInputKind.PointerMove && input.X == 5 && input.Y == 3;
                up |= input.Kind == TerminalInputKind.PointerUp && input.X == 5 && input.Y == 3;
                controlC |= input.Kind == TerminalInputKind.Key
                    && input.Key == new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true);
                unicode |= input.Kind == TerminalInputKind.Text && input.Text == "😀";
                paste |= input.Kind == TerminalInputKind.Paste && input.Text == "cq\u0003";
                if (input.Kind == TerminalInputKind.Key && input.Key.KeyChar == 'q')
                {
                    break;
                }
            }
        }

        bool restored = native.GetInputMode() == inputMode && native.GetOutputMode() == outputMode
            && native.GetCursor().Visible == cursor.Visible && native.GetCursor().Size == cursor.Size;
        System.Console.Write($"\r\nRESULT handoff={handedOff} frame={frameFits} wheel={wheel} down={down} move={move}"
            + $" up={up} ctrlc={controlC} unicode={unicode} paste={paste} restored={restored}\r\n");
        System.Console.WriteLine(string.Join(" ", observed));
        return handedOff && frameFits && wheel && down && move && up && controlC && unicode && paste && restored ? 0 : 11;
    }

    private static unsafe char ReadCell(int x, int y)
    {
        ushort character = 0;
        WindowsCoordinate coordinate = new() { X = (short)x, Y = (short)y };
        if (!ReadConsoleOutputCharacter(GetStdHandle(-11), &character, 1, coordinate, out uint count) || count != 1)
        {
            return '\0';
        }

        return (char)character;
    }

    private static unsafe bool IsIsolatedConsole()
    {
        uint* processes = stackalloc uint[16];
        uint count = GetConsoleProcessList(processes, 16);
        return count == 1 && processes[0] == GetCurrentProcessId();
    }

    private static SafeFileHandle OpenConsole(string name)
    {
        SafeFileHandle handle = CreateFile(name, 0xc0000000, 3, 0, 3, 0, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new InvalidOperationException("The isolated console did not expose native handles.");
        }

        return handle;
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int standardHandle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStdHandle(int standardHandle, SafeFileHandle handle);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string name, uint access, uint sharing, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial uint GetConsoleProcessList(uint* processes, uint count);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32.dll", EntryPoint = "ReadConsoleOutputCharacterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadConsoleOutputCharacter(
        nint output, ushort* character, uint length, WindowsCoordinate coordinate, out uint count);

    private sealed class WindowsPlatform : IPlatform
    {
        public bool IsWindows => true;

        public bool IsMacOS => false;
    }
}
#endif
