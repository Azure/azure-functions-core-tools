// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Formats.Tar;

namespace Azure.Functions.Cli.Common;

/// <inheritdoc cref="IFileSystem" />
internal sealed class PhysicalFileSystem : IFileSystem
{
    // ── File operations ─────────────────────────────────────────────────────

    public bool FileExists(string path) => File.Exists(path);

    public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        EnsureParentDirectory(path);
        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            // Clean up the temp file if Move failed or was never reached.
            try { File.Delete(tempPath); }
            catch { /* Best-effort cleanup — file may already be gone after a successful Move. */ }
        }
    }

    public async Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken)
    {
        await using FileStream file = File.Create(filePath);
        await content.CopyToAsync(file, cancellationToken);
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void CopyFile(string sourcePath, string destinationPath) =>
        File.Copy(sourcePath, destinationPath, overwrite: true);

    public void DeleteFile(string path) => File.Delete(path);

    public Task DeleteFileIfExistsAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    // ── Directory operations ────────────────────────────────────────────────

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public TempDirectory CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return new TempDirectory(path, this);
    }

    public TempDirectory CreateTempDirectory(string parentDirectory)
    {
        string path = Path.Combine(parentDirectory, Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return new TempDirectory(path, this);
    }

    public void CopyDirectory(string sourcePath, string destinationPath)
    {
        DirectoryInfo source = new(sourcePath);
        Directory.CreateDirectory(destinationPath);

        foreach (FileInfo file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(destinationPath, file.Name), overwrite: true);
        }

        foreach (DirectoryInfo subDir in source.GetDirectories())
        {
            CopyDirectory(subDir.FullName, Path.Combine(destinationPath, subDir.Name));
        }
    }

    public void DeleteDirectory(string path) =>
        Directory.Delete(path, recursive: true);

    public IReadOnlyList<string> GetFiles(string directoryPath) =>
        Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);

    public string GetCanonicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException($"Path '{path}' does not have a root.", nameof(path));
        string current = root;

        foreach (string segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(current, segment);
            FileSystemInfo fileSystemInfo = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            FileSystemInfo? target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
            current = target?.FullName ?? candidate;
        }

        return Path.GetFullPath(current);
    }

    // ── Archive operations ──────────────────────────────────────────────────

    public void ExtractZip(string zipPath, string destinationDirectory)
    {
        ValidateExtractionDestination(destinationDirectory);

        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                ValidateArchiveEntry(destinationDirectory, entry.FullName);
                int unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixFileType == 0xA000)
                {
                    throw new InvalidDataException($"Archive entry '{entry.FullName}' is a symbolic link.");
                }
            }
        }

        ZipFile.ExtractToDirectory(zipPath, destinationDirectory);
    }

    public void ExtractTarGz(string tarGzPath, string destinationDirectory)
    {
        ValidateExtractionDestination(destinationDirectory);

        using (FileStream validationFile = File.OpenRead(tarGzPath))
        using (var validationGzip = new GZipStream(validationFile, CompressionMode.Decompress))
        using (TarReader reader = new(validationGzip))
        {
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) is not null)
            {
                ValidateArchiveEntry(destinationDirectory, entry.Name);
                if (entry.EntryType is not TarEntryType.RegularFile
                    and not TarEntryType.V7RegularFile
                    and not TarEntryType.Directory)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.Name}' has unsupported type '{entry.EntryType}'.");
                }
            }
        }

        Directory.CreateDirectory(destinationDirectory);
        using FileStream fileStream = File.OpenRead(tarGzPath);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzipStream, destinationDirectory, overwriteFiles: true);
    }

    // ── Hash operations ─────────────────────────────────────────────────────

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static void EnsureParentDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void ValidateExtractionDestination(string destinationDirectory)
    {
        var destination = new DirectoryInfo(Path.GetFullPath(destinationDirectory));
        if (destination.LinkTarget is not null)
        {
            throw new InvalidDataException($"Archive destination '{destinationDirectory}' is a link.");
        }
    }

    private static void ValidateArchiveEntry(string destinationDirectory, string entryName)
    {
        string normalizedName = entryName.Replace('\\', '/');
        if ((normalizedName.Length > 0 && normalizedName[0] == '/')
            || (normalizedName.Length >= 3
                && char.IsAsciiLetter(normalizedName[0])
                && normalizedName[1] == ':'
                && normalizedName[2] == '/'))
        {
            throw new InvalidDataException($"Archive entry '{entryName}' has an absolute path.");
        }

        string destination = Path.GetFullPath(destinationDirectory);
        string target = Path.GetFullPath(Path.Combine(
            destination,
            normalizedName.Replace('/', Path.DirectorySeparatorChar)));
        string destinationPrefix = Path.TrimEndingDirectorySeparator(destination) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(destinationPrefix, comparison))
        {
            throw new InvalidDataException($"Archive entry '{entryName}' escapes the destination directory.");
        }

        string relativeTarget = Path.GetRelativePath(destination, target);
        string current = destination;
        foreach (string segment in relativeTarget.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (info.LinkTarget is not null)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entryName}' traverses existing link '{current}'.");
            }
        }
    }
}
