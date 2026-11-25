// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace ArtifactAssembler
{
    /// <summary>
    /// Marks entries of an existing zip file as Unix executables.
    /// </summary>
    /// <remarks>
    /// The artifacts are zipped on a Windows agent, where <see cref="System.IO.Compression.ZipFile"/> writes
    /// every entry as created on Windows and without Unix permissions, so <c>func</c> and <c>gozip</c> are
    /// extracted without the executable bit on Linux and macOS. The central directory of the finished zip
    /// is edited in place: for the selected entries the "version made by" platform becomes Unix and the
    /// upper 16 bits of the external attributes carry a regular file with mode 0755.
    /// </remarks>
    internal static class ZipUnixPermissions
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint CentralDirectoryHeaderSignature = 0x02014b50;
        private const int EndOfCentralDirectorySize = 22;
        private const int CentralDirectoryHeaderSize = 46;
        private const byte UnixPlatform = 3;

        // S_IFREG | 0755
        private const uint UnixRegularFileMode755 = 0x81ED;

        /// <summary>
        /// Returns true for the native launchers shipped in the CLI zips.
        /// </summary>
        internal static bool IsExecutableEntry(string entryName)
        {
            string name = entryName.Substring(entryName.LastIndexOf('/') + 1);
            return name == "func" || name == "gozip";
        }

        /// <summary>
        /// Sets the Unix executable mode on every entry of <paramref name="zipPath"/> for which
        /// <paramref name="isExecutable"/> returns true. Returns the number of entries changed.
        /// </summary>
        internal static int MarkExecutables(string zipPath, Func<string, bool> isExecutable)
        {
            using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            (long cdOffset, int cdSize, int entryCount) = ReadEndOfCentralDirectory(stream);

            byte[] cd = new byte[cdSize];
            stream.Seek(cdOffset, SeekOrigin.Begin);
            stream.ReadExactly(cd);

            int changed = 0;
            int position = 0;
            for (int i = 0; i < entryCount; i++)
            {
                if (position + CentralDirectoryHeaderSize > cd.Length
                    || BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(position)) != CentralDirectoryHeaderSignature)
                {
                    throw new InvalidDataException($"Invalid central directory entry {i} in '{zipPath}'.");
                }

                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(position + 28));
                int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(position + 30));
                int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(position + 32));
                string name = Encoding.UTF8.GetString(cd, position + CentralDirectoryHeaderSize, nameLength);

                if (!name.EndsWith('/') && isExecutable(name))
                {
                    // Byte 5 of the header is the platform part of "version made by".
                    cd[position + 5] = UnixPlatform;
                    BinaryPrimitives.WriteUInt32LittleEndian(cd.AsSpan(position + 38), UnixRegularFileMode755 << 16);
                    changed++;
                }

                position += CentralDirectoryHeaderSize + nameLength + extraLength + commentLength;
            }

            if (changed > 0)
            {
                stream.Seek(cdOffset, SeekOrigin.Begin);
                stream.Write(cd);
            }

            return changed;
        }

        private static (long Offset, int Size, int Count) ReadEndOfCentralDirectory(FileStream stream)
        {
            // The end of central directory record is followed by a comment of at most 65535 bytes.
            int searchLength = (int)Math.Min(stream.Length, EndOfCentralDirectorySize + ushort.MaxValue);
            byte[] tail = new byte[searchLength];
            stream.Seek(stream.Length - searchLength, SeekOrigin.Begin);
            stream.ReadExactly(tail);

            for (int i = searchLength - EndOfCentralDirectorySize; i >= 0; i--)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != EndOfCentralDirectorySignature)
                {
                    continue;
                }

                int count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 10));
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 12));
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 16));

                if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
                {
                    throw new NotSupportedException("Zip64 archives are not supported.");
                }

                return (offset, (int)size, count);
            }

            throw new InvalidDataException("End of central directory record not found.");
        }
    }
}
