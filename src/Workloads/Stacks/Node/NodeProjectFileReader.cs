// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Observes bounded project paths without following known links or modifying the project.
/// </summary>
internal partial class NodeProjectFileReader
{
    public virtual async Task<(NodeProjectFileKind Kind, byte[] Bytes)> ReadAsync(
        string projectRoot, string logicalPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalPath);
        string[] components = logicalPath.Split('/');
        if (!Path.IsPathFullyQualified(projectRoot)
            || Path.IsPathRooted(logicalPath)
            || components.Any(component => component is "" or "." or ".." || component.Contains('\\')))
        {
            throw new ArgumentException("Evidence requires a fully qualified root and a bounded relative path.");
        }

        string current = projectRoot;
        int nextComponent = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                return (NodeProjectFileKind.Absent, []);
            }
            catch (DirectoryNotFoundException)
            {
                return (NodeProjectFileKind.Absent, []);
            }

            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            {
                return (NodeProjectFileKind.Unsupported, []);
            }

            bool directory = (attributes & FileAttributes.Directory) != 0;
            if (nextComponent == components.Length)
            {
                if (directory)
                {
                    return (NodeProjectFileKind.Directory, []);
                }

                if (!OperatingSystem.IsWindows() && !IsRegularUnixFile(current))
                {
                    return (NodeProjectFileKind.Unsupported, []);
                }

                byte[] bytes = await File.ReadAllBytesAsync(current, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return (NodeProjectFileKind.RegularFile, bytes);
            }

            if (!directory)
            {
                return (NodeProjectFileKind.Unsupported, []);
            }

            current = Path.Combine(current, components[nextComponent++]);
        }
    }

    private static bool IsRegularUnixFile(string path)
    {
        int result;
        bool regular;
        try
        {
            if (OperatingSystem.IsLinux())
            {
                const int CurrentDirectory = -100;
                const int NoFollow = 0x100;
                const uint TypeInformation = 1;
                const int FileTypeMask = 0xF000;
                const int RegularFileMode = 0x8000;
                result = Statx(CurrentDirectory, path, NoFollow, TypeInformation, out LinuxFileStatus status);
                regular = result == 0 && (status.Mask & TypeInformation) != 0 && (status.Mode & FileTypeMask) == RegularFileMode;
            }
            else if (OperatingSystem.IsMacOS())
            {
                const uint ObjectTypeAttribute = 8;
                const ulong NoFollow = 1;
                const uint RegularVnode = 1;
                MacAttributeList request = new() { BitmapCount = 5, Common = ObjectTypeAttribute };
                int bufferSize = Marshal.SizeOf<MacFileType>();
                result = GetAttrList(path, in request, out MacFileType status, (nuint)bufferSize, NoFollow);
                regular = result == 0 && status.Length == bufferSize && status.Type == RegularVnode;
            }
            else
            {
                return false;
            }
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new NotSupportedException($"The regular-file evidence probe is unavailable for '{path}'.", exception);
        }
        catch (DllNotFoundException exception)
        {
            throw new NotSupportedException($"The regular-file evidence probe is unavailable for '{path}'.", exception);
        }

        if (result != 0)
        {
            int error = Marshal.GetLastPInvokeError();
            Win32Exception cause = new(error);
            if (error is 1 or 13)
            {
                throw new UnauthorizedAccessException($"Cannot inspect evidence file kind at '{path}'.", cause);
            }

            throw new IOException($"Cannot inspect evidence file kind at '{path}'.", cause);
        }

        return regular;
    }

    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(int directory, string path, int flags, uint mask, out LinuxFileStatus status);

    [LibraryImport("libSystem.B.dylib", EntryPoint = "getattrlist", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int GetAttrList(string path, in MacAttributeList request, out MacFileType status, nuint size, ulong options);

    // statx reserves a fixed 256-byte ABI buffer even when only type information is requested.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileStatus
    {
        [FieldOffset(0)]
        public uint Mask;

        [FieldOffset(28)]
        public ushort Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacAttributeList
    {
        public ushort BitmapCount;
        public ushort Reserved;
        public uint Common;
        public uint Volume;
        public uint Directory;
        public uint File;
        public uint Fork;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MacFileType
    {
        public uint Length;
        public uint Type;
    }
}
