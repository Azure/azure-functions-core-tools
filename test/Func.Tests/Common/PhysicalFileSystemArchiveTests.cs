// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Azure.Functions.Cli.Common;
using Xunit;

namespace Azure.Functions.Cli.Tests.Common;

public sealed class PhysicalFileSystemArchiveTests
{
    private readonly PhysicalFileSystem _fileSystem = new();

    [Fact]
    public void ExtractZip_TraversalEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.zip");
        CreateZip(archivePath, ("../outside.txt", "malicious"));

        Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractZip(archivePath, destination));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void ExtractTarGz_TraversalEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.tar.gz");
        CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, "../outside.txt")
        {
            DataStream = Content("malicious"),
        });

        Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractTarGz(archivePath, destination));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void ExtractZip_AbsoluteEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.zip");
        CreateZip(archivePath, (NormalizeArchivePath(outsidePath), "malicious"));

        Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractZip(archivePath, destination));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void ExtractTarGz_AbsoluteEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.tar.gz");
        CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, NormalizeArchivePath(outsidePath))
        {
            DataStream = Content("malicious"),
        });

        Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractTarGz(archivePath, destination));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void ExtractZip_ArchiveLink_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsideDirectory = Path.Combine(root.Path, "outside");
        string outsidePath = Path.Combine(outsideDirectory, "payload.txt");
        Directory.CreateDirectory(outsideDirectory);
        string archivePath = Path.Combine(root.Path, "archive.zip");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            ZipArchiveEntry link = archive.CreateEntry("linked");
            link.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            using (StreamWriter writer = new(link.Open()))
            {
                writer.Write(NormalizeArchivePath(outsideDirectory));
            }

            ZipArchiveEntry payload = archive.CreateEntry("linked/payload.txt");
            using StreamWriter payloadWriter = new(payload.Open());
            payloadWriter.Write("malicious");
        }

        Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractZip(archivePath, destination));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public void ExtractTarGz_ArchiveLink_DoesNotWriteOutsideDestination()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsideDirectory = Path.Combine(root.Path, "outside");
        string outsidePath = Path.Combine(outsideDirectory, "payload.txt");
        Directory.CreateDirectory(outsideDirectory);
        string archivePath = Path.Combine(root.Path, "archive.tar.gz");
        CreateTarGz(
            archivePath,
            new PaxTarEntry(TarEntryType.SymbolicLink, "linked")
            {
                LinkName = NormalizeArchivePath(outsideDirectory),
            },
            new PaxTarEntry(TarEntryType.RegularFile, "linked/payload.txt")
            {
                DataStream = Content("malicious"),
            });

        Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractTarGz(archivePath, destination));

        Assert.False(File.Exists(outsidePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtractArchive_ExistingDestinationLink_DoesNotWriteOutsideDestination(bool zip)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsideDirectory = Path.Combine(root.Path, "outside");
        string outsidePath = Path.Combine(outsideDirectory, "payload.txt");
        Directory.CreateDirectory(outsideDirectory);
        Directory.CreateSymbolicLink(Path.Combine(destination, "linked"), outsideDirectory);

        if (zip)
        {
            string archivePath = Path.Combine(root.Path, "archive.zip");
            CreateZip(archivePath, ("linked/payload.txt", "malicious"));
            Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractZip(archivePath, destination));
        }
        else
        {
            string archivePath = Path.Combine(root.Path, "archive.tar.gz");
            CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, "linked/payload.txt")
            {
                DataStream = Content("malicious"),
            });
            Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractTarGz(archivePath, destination));
        }

        Assert.False(File.Exists(outsidePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtractArchive_LinkedDestinationRoot_DoesNotWriteOutsideDestination(bool zip)
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = Path.Combine(root.Path, "extract");
        string outsideDirectory = Path.Combine(root.Path, "outside");
        string outsidePath = Path.Combine(outsideDirectory, "payload.txt");
        Directory.CreateDirectory(outsideDirectory);
        Directory.CreateSymbolicLink(destination, outsideDirectory);

        if (zip)
        {
            string archivePath = Path.Combine(root.Path, "archive.zip");
            CreateZip(archivePath, ("payload.txt", "malicious"));
            Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractZip(archivePath, destination));
        }
        else
        {
            string archivePath = Path.Combine(root.Path, "archive.tar.gz");
            CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, "payload.txt")
            {
                DataStream = Content("malicious"),
            });
            Assert.Throws<InvalidDataException>(() => _fileSystem.ExtractTarGz(archivePath, destination));
        }

        Assert.False(File.Exists(outsidePath));
    }

    private static string CreateDestination(string root)
    {
        string destination = Path.Combine(root, "extract");
        Directory.CreateDirectory(destination);
        return destination;
    }

    private static void CreateZip(string archivePath, params (string Name, string Content)[] entries)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        foreach ((string name, string content) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(name);
            using StreamWriter writer = new(entry.Open());
            writer.Write(content);
        }
    }

    private static void CreateTarGz(string archivePath, params TarEntry[] entries)
    {
        using FileStream file = File.Create(archivePath);
        using GZipStream gzip = new(file, CompressionLevel.SmallestSize);
        using TarWriter writer = new(gzip, TarEntryFormat.Pax);
        foreach (TarEntry entry in entries)
        {
            writer.WriteEntry(entry);
        }
    }

    private static MemoryStream Content(string value) => new(Encoding.UTF8.GetBytes(value));

    private static string NormalizeArchivePath(string path) => path.Replace('\\', '/');
}
