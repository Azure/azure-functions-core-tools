// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Console.Terminal;

internal sealed partial class MacChildStopSignalScope : IDisposable
{
    private const int ChildSignal = 20;
    private const int NoChildStop = 0x0008;
    private readonly PosixSignalRegistration _registration;
    private readonly MacSignalAction _original;
    private int _disposed;

    public MacChildStopSignalScope()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Darwin child-signal configuration requires macOS.");
        }

        // Keep the runtime's handler installed, including when a child is started later.
        _registration = PosixSignalRegistration.Create(PosixSignal.SIGCHLD, static _ => { });
        try
        {
            Check(GetAction(ChildSignal, 0, out _original));
            MacSignalAction action = _original;
            action.Flags |= NoChildStop;
            Check(SetAction(ChildSignal, in action, 0));
        }
        catch
        {
            _registration.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Check(GetAction(ChildSignal, 0, out MacSignalAction current));
            if (current.Handler == _original.Handler)
            {
                current.Flags = (current.Flags & ~NoChildStop) | (_original.Flags & NoChildStop);
                Check(SetAction(ChildSignal, in current, 0));
            }
        }
        finally
        {
            _registration.Dispose();
        }
    }

    private static void Check(int result)
    {
        if (result < 0)
        {
            throw new IOException("Configuring Darwin child-stop notifications failed.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    [LibraryImport("libc", EntryPoint = "sigaction", SetLastError = true)]
    private static partial int GetAction(int signal, nint action, out MacSignalAction previous);

    [LibraryImport("libc", EntryPoint = "sigaction", SetLastError = true)]
    private static partial int SetAction(int signal, in MacSignalAction action, nint previous);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MacSignalAction
{
    public nint Handler;
    public uint Mask;
    public int Flags;
}
