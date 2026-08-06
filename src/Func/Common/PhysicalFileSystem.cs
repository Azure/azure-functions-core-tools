// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;

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

    public string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateTempDirectory(string siblingPath)
    {
        string fullPath = Path.GetFullPath(siblingPath);
        string parentDir = Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath) ?? string.Empty;

        if (string.IsNullOrEmpty(parentDir))
        {
            return CreateTempDirectory();
        }

        string path = Path.Combine(parentDir, ".func-update-" + Path.GetRandomFileName());
        Directory.CreateDirectory(path);
        return path;
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

    private static void EnsureParentDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
