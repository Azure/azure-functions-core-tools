// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Console.Terminal;

internal interface IWindowsTerminalNative
{
    public bool IsAvailable { get; }

    public uint GetInputMode();

    public uint GetOutputMode();

    public void SetInputMode(uint mode);

    public void SetOutputMode(uint mode);

    public WindowsConsoleWindow GetWindow();

    public WindowsCursorInfo GetCursor();

    public void SetCursor(WindowsCursorInfo cursor);

    public bool WaitForInput(CancellationToken cancellationToken);

    public WindowsInputRecord Read();
}

internal sealed partial class WindowsTerminalNative : IWindowsTerminalNative
{
    private readonly nint _input = GetStdHandle(-10);
    private readonly nint _output = GetStdHandle(-11);

    public bool IsAvailable => GetConsoleMode(_input, out _) && GetConsoleMode(_output, out _);

    public uint GetInputMode()
    {
        Check(GetConsoleMode(_input, out uint mode));
        return mode;
    }

    public uint GetOutputMode()
    {
        Check(GetConsoleMode(_output, out uint mode));
        return mode;
    }

    public void SetInputMode(uint mode) => Check(SetConsoleMode(_input, mode));

    public void SetOutputMode(uint mode) => Check(SetConsoleMode(_output, mode));

    public WindowsConsoleWindow GetWindow()
    {
        Check(GetConsoleScreenBufferInfo(_output, out WindowsScreenBufferInfo info));
        return new WindowsConsoleWindow(
            info.Window.Left, info.Window.Top,
            info.Window.Right - info.Window.Left + 1, info.Window.Bottom - info.Window.Top + 1);
    }

    public WindowsCursorInfo GetCursor()
    {
        Check(GetConsoleCursorInfo(_output, out WindowsCursorInfo cursor));
        return cursor;
    }

    public void SetCursor(WindowsCursorInfo cursor) => Check(SetConsoleCursorInfo(_output, in cursor));

    public unsafe bool WaitForInput(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nint* handles = stackalloc nint[2];
        handles[0] = _input;
        handles[1] = cancellationToken.WaitHandle.SafeWaitHandle.DangerousGetHandle();
        uint result = WaitForMultipleObjects(2, handles, false, 50);
        cancellationToken.ThrowIfCancellationRequested();
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Waiting for terminal input failed.");
        }

        return result == 0;
    }

    public WindowsInputRecord Read()
    {
        Check(ReadConsoleInput(_input, out WindowsInputRecord record, 1, out uint count));
        if (count != 1)
        {
            throw new EndOfStreamException("The terminal input stream closed.");
        }

        return record;
    }

    private static void Check(bool success)
    {
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows terminal operation failed.");
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int standardHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint handle, out uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint handle, uint mode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleScreenBufferInfo(nint handle, out WindowsScreenBufferInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleCursorInfo(nint handle, out WindowsCursorInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleCursorInfo(nint handle, in WindowsCursorInfo info);

    [LibraryImport("kernel32.dll", EntryPoint = "ReadConsoleInputW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadConsoleInput(nint handle, out WindowsInputRecord record, uint length, out uint count);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static unsafe partial uint WaitForMultipleObjects(
        uint count, nint* handles, [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);
}

internal readonly record struct WindowsConsoleWindow(int Left, int Top, int Width, int Height);

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsCoordinate
{
    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsRectangle
{
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsScreenBufferInfo
{
    public WindowsCoordinate Size;
    public WindowsCoordinate CursorPosition;
    public ushort Attributes;
    public WindowsRectangle Window;
    public WindowsCoordinate MaximumWindowSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsCursorInfo
{
    public uint Size;
    public int Visible;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsKeyRecord
{
    public int KeyDown;
    public ushort RepeatCount;
    public ushort VirtualKeyCode;
    public ushort VirtualScanCode;
    public ushort UnicodeCharacter;
    public uint ControlKeyState;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsMouseRecord
{
    public WindowsCoordinate Position;
    public uint ButtonState;
    public uint ControlKeyState;
    public uint EventFlags;
}

[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct WindowsInputRecord
{
    [FieldOffset(0)]
    public ushort EventType;

    [FieldOffset(4)]
    public WindowsKeyRecord Key;

    [FieldOffset(4)]
    public WindowsMouseRecord Mouse;

    [FieldOffset(4)]
    public int Focused;
}
