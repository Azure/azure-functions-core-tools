// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;

namespace Azure.Functions.Cli.Common;

/// <inheritdoc cref="IFileSystem" />
internal class PhysicalFileSystem : IFileSystem
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

    public Stream? OpenReadIfExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        => WriteAllTextAsync(path, contents, overwrite: true, cancellationToken);

    public Task CreateAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        => WriteAllTextAsync(path, contents, overwrite: false, cancellationToken);

    private async Task WriteAllTextAsync(string path, string contents, bool overwrite, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);
        cancellationToken.ThrowIfCancellationRequested();

        EnsureParentDirectory(path);
        string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await File.WriteAllTextAsync(tempPath, contents, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            MoveFile(tempPath, path, overwrite);
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

    public virtual void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
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

    public async Task CopyDirectoryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        DirectoryInfo source = new(sourcePath);
        Directory.CreateDirectory(destinationPath);
        foreach (FileInfo file in source.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destination = Path.Combine(destinationPath, file.Name);
            await using (FileStream input = file.OpenRead())
            {
                await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.SetLastWriteTimeUtc(destination, file.LastWriteTimeUtc);
        }

        foreach (DirectoryInfo directory in source.GetDirectories())
        {
            await CopyDirectoryAsync(directory.FullName, Path.Combine(destinationPath, directory.Name), cancellationToken);
        }
    }

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
