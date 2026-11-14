// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Console.Clipboard;

internal interface IWindowsClipboardNative
{
    public bool IsLocalSession { get; }

    public nint CreateOwnerWindow();

    public void DestroyOwnerWindow(nint window);

    public nint Allocate(int bytes);

    public void Free(nint allocation);

    public void Write(nint allocation, string text);

    public bool TryOpen(nint window);

    public void WaitBeforeRetry(CancellationToken cancellationToken);

    public void Empty();

    public void SetUnicodeData(nint allocation);

    public void Close();
}

internal sealed partial class WindowsClipboardNative : IWindowsClipboardNative
{
    public bool IsLocalSession
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            uint active = WtsGetActiveConsoleSessionId();
            return active is not (0 or uint.MaxValue)
                && ProcessIdToSessionId(GetCurrentProcessId(), out uint current) && current == active;
        }
    }

    public nint CreateOwnerWindow()
    {
        nint window = CreateWindowEx(0, "STATIC", string.Empty, 0, 0, 0, 0, 0, new nint(-3), 0, 0, 0);
        Check(window != 0);
        return window;
    }

    public void DestroyOwnerWindow(nint window) => DestroyWindow(window);

    public nint Allocate(int bytes)
    {
        nint allocation = GlobalAlloc(0x0042, (nuint)bytes);
        Check(allocation != 0);
        return allocation;
    }

    public void Free(nint allocation) => GlobalFree(allocation);

    public void Write(nint allocation, string text)
    {
        nint pointer = GlobalLock(allocation);
        Check(pointer != 0);
        try
        {
            Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
            Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
        }
        finally
        {
            GlobalUnlock(allocation);
        }
    }

    public bool TryOpen(nint window) => OpenClipboard(window);

    public void WaitBeforeRetry(CancellationToken cancellationToken)
    {
        cancellationToken.WaitHandle.WaitOne(25);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Empty() => Check(EmptyClipboard());

    public void SetUnicodeData(nint allocation) => Check(SetClipboardData(13, allocation) != 0);

    public void Close() => Check(CloseClipboard());

    private static void Check(bool success)
    {
        if (!success)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "WTSGetActiveConsoleSessionId")]
    private static partial uint WtsGetActiveConsoleSessionId();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint extendedStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint allocation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint allocation);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint allocation);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint data);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();
}
