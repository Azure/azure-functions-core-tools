// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Buffers.Binary;

namespace Azure.Functions.Cli.Console.Terminal;

/// <summary>
/// Uses the LP64 glibc and Darwin termios layouts shared by each OS's x64 and arm64 ABIs.
/// </summary>
internal static class UnixTerminalMode
{
    internal const int LinuxSize = 60;
    internal const int DarwinSize = 72;
    internal const int LinuxControlCharacters = 17;
    internal const int DarwinControlCharacters = 32;

    public static byte[] CreateInputMode(byte[] original, bool isMacOS)
    {
        ArgumentNullException.ThrowIfNull(original);
        int size = isMacOS ? DarwinSize : LinuxSize;
        if (original.Length != size)
        {
            throw new ArgumentException("The terminal attribute snapshot has the wrong native size.", nameof(original));
        }

        byte[] mode = (byte[])original.Clone();
        if (isMacOS)
        {
            ulong input = BinaryPrimitives.ReadUInt64LittleEndian(mode);
            ulong local = BinaryPrimitives.ReadUInt64LittleEndian(mode.AsSpan(24));
            BinaryPrimitives.WriteUInt64LittleEndian(mode, input & ~0x20UL);
            BinaryPrimitives.WriteUInt64LittleEndian(mode.AsSpan(24), local & ~(0x100UL | 0x8UL | 0x10UL));
            mode[DarwinControlCharacters + 8] = 0xff; // Darwin's _POSIX_VDISABLE, for VINTR only.
            mode[DarwinControlCharacters + 16] = 0;
            mode[DarwinControlCharacters + 17] = 0;
        }
        else
        {
            uint input = BinaryPrimitives.ReadUInt32LittleEndian(mode);
            uint local = BinaryPrimitives.ReadUInt32LittleEndian(mode.AsSpan(12));
            BinaryPrimitives.WriteUInt32LittleEndian(mode, input & ~0x20U);
            BinaryPrimitives.WriteUInt32LittleEndian(mode.AsSpan(12), local & ~(0x2U | 0x8U | 0x40U));
            mode[LinuxControlCharacters] = 0; // Linux's _POSIX_VDISABLE, for VINTR only.
            mode[LinuxControlCharacters + 6] = 0;
            mode[LinuxControlCharacters + 5] = 0;
        }

        return mode;
    }
}
