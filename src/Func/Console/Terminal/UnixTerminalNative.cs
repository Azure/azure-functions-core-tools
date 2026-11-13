// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Console.Terminal;

internal interface IUnixTerminalNative
{
    public bool IsAvailable { get; }

    public TerminalSize GetSize();

    public byte[] GetAttributes();

    public void SetAttributes(byte[] attributes);

    public short Poll(int milliseconds);

    public int Read(byte[] buffer);

    public void SuspendProcess();
}

internal sealed partial class UnixTerminalNative(bool isMacOS) : IUnixTerminalNative
{
    private readonly bool _isMacOS = isMacOS;

    public bool IsAvailable => (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        && RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64
        && IsATty(0) == 1 && IsATty(1) == 1;

    public TerminalSize GetSize()
    {
        nuint request = _isMacOS ? 0x40087468U : 0x5413U;
        UnixWindowSize size;
        if (_isMacOS && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            // Darwin arm64 puts variadic arguments on the stack, unlike Linux arm64 and x64.
            Check(IoctlDarwinArm64(1, request, 0, 0, 0, 0, 0, 0, out size));
        }
        else
        {
            Check(Ioctl(1, request, out size));
        }

        return new TerminalSize(size.Columns, size.Rows);
    }

    public unsafe byte[] GetAttributes()
    {
        byte[] attributes = new byte[_isMacOS ? UnixTerminalMode.DarwinSize : UnixTerminalMode.LinuxSize];
        fixed (byte* pointer = attributes)
        {
            Check(TcGetAttr(0, pointer));
        }

        return attributes;
    }

    public unsafe void SetAttributes(byte[] attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (attributes.Length != (_isMacOS ? UnixTerminalMode.DarwinSize : UnixTerminalMode.LinuxSize))
        {
            throw new ArgumentException("The terminal attribute snapshot has the wrong native size.", nameof(attributes));
        }

        fixed (byte* pointer = attributes)
        {
            // TCSANOW deliberately does not flush type-ahead.
            Check(TcSetAttr(0, 0, pointer));
        }
    }

    public short Poll(int milliseconds)
    {
        UnixPollDescriptor descriptor = new() { Descriptor = 0, Events = 1 };
        int result = _isMacOS
            ? PollDarwin(ref descriptor, 1, milliseconds)
            : PollLinux(ref descriptor, 1, milliseconds);
        if (result < 0 && Marshal.GetLastPInvokeError() == 4)
        {
            return 0;
        }

        Check(result);
        return result == 0 ? (short)0 : descriptor.ReturnedEvents;
    }

    public unsafe int Read(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        fixed (byte* pointer = buffer)
        {
            nint count = ReadNative(0, pointer, (nuint)buffer.Length);
            if (count < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is 4 or 11 or 35)
                {
                    return 0;
                }

                throw new IOException("Reading the terminal failed.", new Win32Exception(error));
            }

            return (int)count;
        }
    }

    public void SuspendProcess()
        => Check(Kill(Environment.ProcessId, _isMacOS ? 17 : 19));

    private static void Check(int result)
    {
        if (result < 0)
        {
            throw new IOException("The POSIX terminal operation failed.", new Win32Exception(Marshal.GetLastPInvokeError()));
        }
    }

    [LibraryImport("libc", EntryPoint = "isatty", SetLastError = true)]
    private static partial int IsATty(int descriptor);

    [LibraryImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    private static unsafe partial int TcGetAttr(int descriptor, byte* attributes);

    [LibraryImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    private static unsafe partial int TcSetAttr(int descriptor, int action, byte* attributes);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int descriptor, nuint request, out UnixWindowSize size);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int IoctlDarwinArm64(
        int descriptor, nuint request, nint register2, nint register3, nint register4, nint register5, nint register6, nint register7,
        out UnixWindowSize size);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static partial int PollLinux(ref UnixPollDescriptor descriptor, nuint count, int milliseconds);

    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static partial int PollDarwin(ref UnixPollDescriptor descriptor, uint count, int milliseconds);

    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static unsafe partial nint ReadNative(int descriptor, byte* buffer, nuint count);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnixWindowSize
{
    public ushort Rows;
    public ushort Columns;
    public ushort XPixel;
    public ushort YPixel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct UnixPollDescriptor
{
    public int Descriptor;
    public short Events;
    public short ReturnedEvents;
}
