// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;

namespace Azure.Functions.Cli.Common;

/// <inheritdoc cref="IFileSystem" />
internal sealed class PhysicalFileSystem : IFileSystem
{
    private const int MaximumLinkCount = 64;

    // ── File operations ─────────────────────────────────────────────────────

    public bool FileExists(string path) => File.Exists(path);

    public string ResolvePhysicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

if (!Path.IsPathFullyQualified(path))
{
    throw new ArgumentException("Path must be fully qualified.", nameof(path));
}

string fullPath = Path.GetFullPath(path);

        Queue<string> remainingSegments = new(GetPathSegments(fullPath));
        string resolvedPath = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("Path does not have a root.", nameof(path));
        HashSet<string> visitedLinks = new(GetPathComparer());
        int followedLinkCount = 0;

        while (remainingSegments.TryDequeue(out string? segment))
        {
            string candidatePath = Path.Combine(resolvedPath, segment);
            FileSystemInfo pathInfo = Directory.Exists(candidatePath)
                ? new DirectoryInfo(candidatePath)
                : new FileInfo(candidatePath);
            FileSystemInfo? target = pathInfo.ResolveLinkTarget(returnFinalTarget: false);

            if (target is null)
            {
                if (!pathInfo.Exists)
                {
                    throw new FileNotFoundException(
                        $"Path component '{candidatePath}' does not exist while resolving '{path}'.",
                        candidatePath);
                }

                if ((pathInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Reparse point '{candidatePath}' cannot be resolved.");
                }

                resolvedPath = candidatePath;
                continue;
            }

            if (!visitedLinks.Add(candidatePath) || ++followedLinkCount > MaximumLinkCount)
            {
                throw new IOException($"A symbolic-link or junction cycle was detected while resolving '{path}'.");
            }

            string targetPath = Path.GetFullPath(target.FullName);
            string targetRoot = Path.GetPathRoot(targetPath)
                ?? throw new IOException($"Link target '{targetPath}' does not have a root.");
            Queue<string> targetSegments = new(GetPathSegments(targetPath));
            while (remainingSegments.TryDequeue(out string? remainingSegment))
            {
                targetSegments.Enqueue(remainingSegment);
            }

            remainingSegments = targetSegments;
            resolvedPath = targetRoot;
        }

        return resolvedPath;
    }

    public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task<byte[]?> ReadAllBytesIfExistsAsync(string path, int maxByteCount, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxByteCount);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[maxByteCount + 1];
        int bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(bytesRead), cancellationToken);
            if (count == 0)
            {
                return buffer[..bytesRead];
            }

            bytesRead += count;
        }

        throw new InvalidDataException($"File '{path}' exceeds the {maxByteCount}-byte read limit.");
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

    // ── Archive operations ──────────────────────────────────────────────────

    public void ExtractZip(string zipPath, string destinationDirectory) =>
        ZipFile.ExtractToDirectory(zipPath, destinationDirectory);

    public void ExtractTarGz(string tarGzPath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        using FileStream fileStream = File.OpenRead(tarGzPath);
        using var gzipStream = new System.IO.Compression.GZipStream(fileStream, CompressionMode.Decompress);
        System.Formats.Tar.TarFile.ExtractToDirectory(gzipStream, destinationDirectory, overwriteFiles: true);
    }

    // ── Hash operations ─────────────────────────────────────────────────────

    public async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(filePath);
        byte[] hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static IEnumerable<string> GetPathSegments(string path)
    {
        string root = Path.GetPathRoot(path)
            ?? throw new ArgumentException("Path does not have a root.", nameof(path));
        return path[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void EnsureParentDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
