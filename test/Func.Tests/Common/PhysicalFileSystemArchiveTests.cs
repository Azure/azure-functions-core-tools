// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Azure.Functions.Cli.Common;
using Xunit;
using Xunit.Sdk;

namespace Azure.Functions.Cli.Tests.Common;

public sealed class PhysicalFileSystemArchiveTests
{
    private readonly PhysicalFileSystem _fileSystem = new();

    [Fact]
    public async Task ExtractZip_TraversalEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.zip");
        CreateZip(archivePath, ("../outside.txt", "malicious"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _fileSystem.ExtractZipAsync(archivePath, destination, CancellationToken.None));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task ExtractTarGz_TraversalEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.tar.gz");
        CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, "../outside.txt")
        {
            DataStream = Content("malicious"),
        });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _fileSystem.ExtractTarGzAsync(archivePath, destination, CancellationToken.None));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task ExtractZip_AbsoluteEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.zip");
        CreateZip(archivePath, (NormalizeArchivePath(outsidePath), "malicious"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _fileSystem.ExtractZipAsync(archivePath, destination, CancellationToken.None));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task ExtractTarGz_AbsoluteEntry_DoesNotWriteOutsideDestination()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsidePath = Path.Combine(root.Path, "outside.txt");
        string archivePath = Path.Combine(root.Path, "archive.tar.gz");
        CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, NormalizeArchivePath(outsidePath))
        {
            DataStream = Content("malicious"),
        });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _fileSystem.ExtractTarGzAsync(archivePath, destination, CancellationToken.None));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task ExtractTarGz_RootDirectoryEntry_ExtractsContents()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string archivePath = Path.Combine(root.Path, "archive.tar.gz");
        CreateTarGz(
            archivePath,
            new PaxTarEntry(TarEntryType.Directory, "./"),
            new PaxTarEntry(TarEntryType.RegularFile, "payload.txt")
            {
                DataStream = Content("expected"),
            });

        await _fileSystem.ExtractTarGzAsync(archivePath, destination, CancellationToken.None);

        Assert.Equal("expected", File.ReadAllText(Path.Combine(destination, "payload.txt")));
    }

    [Fact]
    public async Task ExtractTarGz_BareRootDirectoryEntry_DoesNotPartiallyExtract()
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string payloadPath = Path.Combine(destination, "payload.txt");
        string archivePath = Path.Combine(root.Path, "archive.tar.gz");
        CreateTarGz(
            archivePath,
            new PaxTarEntry(TarEntryType.Directory, "."),
            new PaxTarEntry(TarEntryType.RegularFile, "payload.txt")
            {
                DataStream = Content("unexpected"),
            });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _fileSystem.ExtractTarGzAsync(archivePath, destination, CancellationToken.None));

        Assert.False(File.Exists(payloadPath));
    }

    [Fact]
    public async Task ExtractZip_ArchiveLink_DoesNotWriteOutsideDestination()
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

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _fileSystem.ExtractZipAsync(archivePath, destination, CancellationToken.None));

        Assert.False(File.Exists(outsidePath));
    }

    [Fact]
    public async Task ExtractTarGz_ArchiveLink_DoesNotWriteOutsideDestination()
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

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _fileSystem.ExtractTarGzAsync(archivePath, destination, CancellationToken.None));

        Assert.False(File.Exists(outsidePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractArchive_ExistingDestinationLink_DoesNotWriteOutsideDestination(bool zip)
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string outsideDirectory = Path.Combine(root.Path, "outside");
        string outsidePath = Path.Combine(outsideDirectory, "payload.txt");
        Directory.CreateDirectory(outsideDirectory);
        CreateDirectorySymbolicLinkOrSkip(Path.Combine(destination, "linked"), outsideDirectory);

        if (zip)
        {
            string archivePath = Path.Combine(root.Path, "archive.zip");
            CreateZip(archivePath, ("linked/payload.txt", "malicious"));
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _fileSystem.ExtractZipAsync(archivePath, destination, CancellationToken.None));
        }
        else
        {
            string archivePath = Path.Combine(root.Path, "archive.tar.gz");
            CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, "linked/payload.txt")
            {
                DataStream = Content("malicious"),
            });
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _fileSystem.ExtractTarGzAsync(archivePath, destination, CancellationToken.None));
        }

        Assert.False(File.Exists(outsidePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractArchive_LinkedDestinationRoot_DoesNotWriteOutsideDestination(bool zip)
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = Path.Combine(root.Path, "extract");
        string outsideDirectory = Path.Combine(root.Path, "outside");
        string outsidePath = Path.Combine(outsideDirectory, "payload.txt");
        Directory.CreateDirectory(outsideDirectory);
        CreateDirectorySymbolicLinkOrSkip(destination, outsideDirectory);

        if (zip)
        {
            string archivePath = Path.Combine(root.Path, "archive.zip");
            CreateZip(archivePath, ("payload.txt", "malicious"));
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _fileSystem.ExtractZipAsync(archivePath, destination, CancellationToken.None));
        }
        else
        {
            string archivePath = Path.Combine(root.Path, "archive.tar.gz");
            CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, "payload.txt")
            {
                DataStream = Content("malicious"),
            });
            await Assert.ThrowsAsync<InvalidDataException>(
                () => _fileSystem.ExtractTarGzAsync(archivePath, destination, CancellationToken.None));
        }

        Assert.False(File.Exists(outsidePath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtractArchive_CanceledToken_ThrowsOperationCanceledException(bool zip)
    {
        using TempDirectory root = _fileSystem.CreateTempDirectory();
        string destination = CreateDestination(root.Path);
        string archivePath = Path.Combine(root.Path, zip ? "archive.zip" : "archive.tar.gz");
        if (zip)
        {
            CreateZip(archivePath, ("payload.txt", "content"));
        }
        else
        {
            CreateTarGz(archivePath, new PaxTarEntry(TarEntryType.RegularFile, "payload.txt")
            {
                DataStream = Content("content"),
            });
        }

        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Func<Task> act = zip
            ? () => _fileSystem.ExtractZipAsync(archivePath, destination, cancellationSource.Token)
            : () => _fileSystem.ExtractTarGzAsync(archivePath, destination, cancellationSource.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
    }

    private static string CreateDestination(string root)
    {
        string destination = Path.Combine(root, "extract");
        Directory.CreateDirectory(destination);
        return destination;
    }

    private static void CreateDirectorySymbolicLinkOrSkip(string path, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(path, targetPath);
        }
        catch (Exception ex) when (
            OperatingSystem.IsWindows()
            && ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            throw SkipException.ForSkip($"Creating symbolic links is not available on this Windows host: {ex.Message}");
        }
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
