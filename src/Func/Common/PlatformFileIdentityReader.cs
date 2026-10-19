// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Azure.Functions.Cli.Common;

internal readonly record struct FileIdentity(ulong Device, ulong Low, ulong High, uint LinkCount, bool IsRegular)
{
    public bool Identifies(FileIdentity other) => Device == other.Device && Low == other.Low && High == other.High;
}

internal interface IFileIdentityReader
{
    public FileIdentity Read(string absolutePath);

    public FileIdentity Read(SafeFileHandle handle);
}

/// <summary>
/// Reads file identity and link count through public platform APIs without relying on runtime-private stat layouts.
/// </summary>
internal sealed partial class PlatformFileIdentityReader : IFileIdentityReader
{
    private const int LinuxPathOnly = 0x200000;
    private const int LinuxCloseOnExec = 0x80000;
    private const int LinuxNoFollow = 0x20000;
    private const uint MacPackInvalidAttributes = 8;
    private const uint MacNoFollow = 1;

    public FileIdentity Read(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            throw new ArgumentException("File identity requires an absolute path.", nameof(absolutePath));
        }
        if (OperatingSystem.IsLinux())
        {
            int descriptor;
            try
            {
                descriptor = OpenLinuxMetadata(absolutePath, LinuxPathOnly | LinuxCloseOnExec | LinuxNoFollow);
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                throw new InvalidDataException("The platform metadata-only file open is unavailable. Selected-file append was refused.", exception);
            }
            if (descriptor < 0) throw PathFailure(absolutePath);
            using SafeFileHandle handle = new((IntPtr)descriptor, ownsHandle: true);
            return Read(handle);
        }
        if (OperatingSystem.IsMacOS())
        {
            MacAttributes attributes = MacIdentityAttributes();
            MacIdentity identity;
            try
            {
                if (ReadMacPath(absolutePath, in attributes, out identity, 44, MacPackInvalidAttributes | MacNoFollow) != 0)
                {
                    throw PathFailure(absolutePath);
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                throw new InvalidDataException("The platform pathname metadata API is unavailable. Selected-file append was refused.", exception);
            }
            return QualifyMac(identity.Length, identity.ReturnedCommon, identity.ReturnedFile,
                identity.Device, identity.ObjectType, identity.Inode, identity.LinkCount);
        }
        if (OperatingSystem.IsWindows())
        {
            using SafeFileHandle handle = File.OpenHandle(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(handle);
        }
        throw new InvalidDataException("File identity and link-count checks are not supported on this platform.");
    }

    public FileIdentity Read(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (handle.IsInvalid || handle.IsClosed)
        {
            throw new ArgumentException("File identity requires an open file handle.", nameof(handle));
        }
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!ReadWindowsIdentity(handle, 18, out WindowsIdentity identity, 24)
                    || !ReadWindowsStandard(handle, 1, out WindowsStandard standard, 24))
                {
                    throw NativeFailure();
                }
                return new(identity.Device, identity.Low, identity.High, standard.LinkCount,
                    standard.Directory == 0 && standard.DeletePending == 0);
            }

            bool retained = false;
            try
            {
                handle.DangerousAddRef(ref retained);
                int descriptor = handle.DangerousGetHandle().ToInt32();
                if (OperatingSystem.IsLinux())
                {
                    const uint required = 0x105;
                    if (ReadLinux(descriptor, string.Empty, 0x1000, required, out LinuxIdentity identity) != 0)
                    {
                        throw NativeFailure();
                    }
                    return QualifyLinux(identity.Mask, identity.LinkCount, identity.Mode, identity.Inode, identity.DeviceMajor, identity.DeviceMinor);
                }
                if (OperatingSystem.IsMacOS())
                {
                    MacAttributes attributes = MacIdentityAttributes();
                    if (ReadMac(descriptor, in attributes, out MacIdentity identity, 44, MacPackInvalidAttributes) != 0)
                    {
                        throw NativeFailure();
                    }
                    return QualifyMac(identity.Length, identity.ReturnedCommon, identity.ReturnedFile,
                        identity.Device, identity.ObjectType, identity.Inode, identity.LinkCount);
                }
                throw new InvalidDataException("File identity and link-count checks are not supported on this platform.");
            }
            finally
            {
                if (retained) handle.DangerousRelease();
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            throw new InvalidDataException("The platform file identity API is unavailable. Selected-file append was refused.", exception);
        }
    }

    internal static FileIdentity QualifyLinux(uint mask, uint links, ushort mode, ulong inode, uint deviceMajor, uint deviceMinor)
        => (mask & 0x105) == 0x105 ? new(((ulong)deviceMajor << 32) | deviceMinor, inode, 0, links, (mode & 0xf000) == 0x8000)
            : throw new InvalidDataException("The filesystem did not provide the required file identity and link-count fields.");

    internal static FileIdentity QualifyMac(uint length, uint common, uint file, uint device, uint type, ulong inode, uint links)
        => length == 44 && (common & 0x0200000a) == 0x0200000a && (file & 1) == 1
            ? new(device, inode, 0, links, type == 1)
            : throw new InvalidDataException("The filesystem did not provide the required file identity and link-count attributes.");

    private static MacAttributes MacIdentityAttributes() => new() { Count = 5, Common = 0x8200000a, File = 1 };

    private static IOException NativeFailure() => new(
        "Cannot read the file identity and hard-link count.", new Win32Exception(Marshal.GetLastPInvokeError()));

    private static Exception PathFailure(string path)
    {
        int error = Marshal.GetLastPInvokeError();
        return error switch
        {
            2 => new FileNotFoundException("The file identity target no longer exists.", path),
            13 => new UnauthorizedAccessException($"Cannot read file identity for '{path}'."),
            20 => new DirectoryNotFoundException($"The file identity target's parent is not a directory: '{path}'."),
            _ => new IOException("Cannot query pathname metadata without opening file content.", new Win32Exception(error)),
        };
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadWindowsIdentity(SafeFileHandle handle, int kind, out WindowsIdentity information, uint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadWindowsStandard(SafeFileHandle handle, int kind, out WindowsStandard information, uint length);

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int ReadLinux(int descriptor, string path, int flags, uint mask, out LinuxIdentity information);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenLinuxMetadata(string path, int flags);

    [LibraryImport("libSystem.B.dylib", EntryPoint = "fgetattrlist", SetLastError = true)]
    private static partial int ReadMac(int descriptor, in MacAttributes attributes, out MacIdentity information, nuint length, nuint options);

    [LibraryImport("libSystem.B.dylib", EntryPoint = "getattrlist", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int ReadMacPath(string path, in MacAttributes attributes, out MacIdentity information, nuint length, nuint options);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsIdentity
    {
        public ulong Device;
        public ulong Low;
        public ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsStandard
    {
        public long Allocation;
        public long Length;
        public uint LinkCount;
        public byte DeletePending;
        public byte Directory;
    }

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxIdentity
    {
        [FieldOffset(0)]
        public uint Mask;
        [FieldOffset(16)]
        public uint LinkCount;
        [FieldOffset(28)]
        public ushort Mode;
        [FieldOffset(32)]
        public ulong Inode;
        [FieldOffset(136)]
        public uint DeviceMajor;
        [FieldOffset(140)]
        public uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MacAttributes
    {
        public ushort Count;
        public ushort Reserved;
        public uint Common;
        public uint Volume;
        public uint Directory;
        public uint File;
        public uint Fork;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MacIdentity
    {
        public uint Length;
        public uint ReturnedCommon;
        public uint ReturnedVolume;
        public uint ReturnedDirectory;
        public uint ReturnedFile;
        public uint ReturnedFork;
        public uint Device;
        public uint ObjectType;
        public ulong Inode;
        public uint LinkCount;
    }
}
