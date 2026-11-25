// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using ArtifactAssembler;
using AwesomeAssertions;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ArtifactAssemblerTests
{
    public class ZipUnixPermissionsTests : IDisposable
    {
        private const uint UnixRegularFileMode755 = 0x81ED;

        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public ZipUnixPermissionsTests()
        {
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            Directory.Delete(_tempDir, recursive: true);
        }

        [Theory]
        [InlineData("func", true)]
        [InlineData("gozip", true)]
        [InlineData("in-proc6/func", true)]
        [InlineData("in-proc8/gozip", true)]
        [InlineData("func.dll", false)]
        [InlineData("workers/python/func", true)]
        [InlineData("myfunc", false)]
        [InlineData("in-proc8/", false)]
        public void IsExecutableEntry_MatchesLaunchers(string entryName, bool expected)
        {
            ZipUnixPermissions.IsExecutableEntry(entryName).Should().Be(expected);
        }

        [Fact]
        public void MarkExecutables_SetsUnixModeOnlyOnSelectedEntries()
        {
            string zipPath = Path.Combine(_tempDir, "test.zip");
            string[] names = { "func", "func.dll", "gozip", "in-proc8/func", "in-proc8/", "workers/placeholder.txt" };

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (string name in names)
                {
                    ZipArchiveEntry entry = archive.CreateEntry(name);
                    if (!name.EndsWith('/'))
                    {
                        using var writer = new StreamWriter(entry.Open());
                        writer.Write("content of " + name);
                    }
                }
            }

            // ZipFile on Windows writes entries as made by the Windows platform; do the same here so the test also runs on Unix.
            SetPlatformOfAllEntries(zipPath, platform: 0);

            int changed = ZipUnixPermissions.MarkExecutables(zipPath, ZipUnixPermissions.IsExecutableEntry);

            changed.Should().Be(3);

            using var result = ZipFile.OpenRead(zipPath);
            result.Entries.Should().HaveCount(names.Length);
            Dictionary<string, byte> platforms = ReadPlatforms(zipPath);
            foreach (ZipArchiveEntry entry in result.Entries)
            {
                bool expectExecutable = ZipUnixPermissions.IsExecutableEntry(entry.FullName);
                (((uint)entry.ExternalAttributes >> 16) == UnixRegularFileMode755).Should().Be(expectExecutable, entry.FullName);

                // A Unix mode is only honoured by unzip when the entry was made by the Unix platform (3).
                platforms[entry.FullName].Should().Be(expectExecutable ? (byte)3 : (byte)0, entry.FullName);

                // The file contents must be untouched.
                if (!entry.FullName.EndsWith('/'))
                {
                    using var reader = new StreamReader(entry.Open());
                    reader.ReadToEnd().Should().Be("content of " + entry.FullName);
                }
            }
        }

        private static void SetPlatformOfAllEntries(string zipPath, byte platform)
        {
            byte[] bytes = File.ReadAllBytes(zipPath);
            foreach (int position in CentralDirectoryHeaders(bytes))
            {
                bytes[position + 5] = platform;
            }

            File.WriteAllBytes(zipPath, bytes);
        }

        private static Dictionary<string, byte> ReadPlatforms(string zipPath)
        {
            byte[] bytes = File.ReadAllBytes(zipPath);
            var result = new Dictionary<string, byte>();
            foreach (int position in CentralDirectoryHeaders(bytes))
            {
                int nameLength = BitConverter.ToUInt16(bytes, position + 28);
                result[System.Text.Encoding.UTF8.GetString(bytes, position + 46, nameLength)] = bytes[position + 5];
            }

            return result;
        }

        private static IEnumerable<int> CentralDirectoryHeaders(byte[] bytes)
        {
            int eocd = bytes.Length - 22;
            while (BitConverter.ToUInt32(bytes, eocd) != 0x06054b50)
            {
                eocd--;
            }

            int count = BitConverter.ToUInt16(bytes, eocd + 10);
            int position = (int)BitConverter.ToUInt32(bytes, eocd + 16);
            for (int i = 0; i < count; i++)
            {
                yield return position;
                position += 46 + BitConverter.ToUInt16(bytes, position + 28) + BitConverter.ToUInt16(bytes, position + 30) + BitConverter.ToUInt16(bytes, position + 32);
            }
        }
    }
}
