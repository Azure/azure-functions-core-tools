// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Common;

/// <inheritdoc cref="IFileSystem" />
internal sealed partial class PhysicalFileSystem : IFileSystem
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

    public FileAttributes? GetAttributesIfExists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            return File.GetAttributes(path);
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

    public Stream CreateNewFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
    }

    public async Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken)
    {
        await using FileStream file = File.Create(filePath);
        await content.CopyToAsync(file, cancellationToken);
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void PublishFileNoReplace(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Path.IsPathFullyQualified(sourcePath) || sourcePath.Contains('\0'))
        {
            throw new ArgumentException("The staged file must have a fully qualified path without null characters.", nameof(sourcePath));
        }

        if (!Path.IsPathFullyQualified(destinationPath) || destinationPath.Contains('\0'))
        {
            throw new ArgumentException("The publication destination must have a fully qualified path without null characters.", nameof(destinationPath));
        }

        string? stagingDirectory = Path.GetDirectoryName(sourcePath);
        if (stagingDirectory is null
            || !string.Equals(Path.GetDirectoryName(stagingDirectory), Path.GetDirectoryName(destinationPath), StringComparison.Ordinal))
        {
            throw new ArgumentException("The staged file must be inside a staging directory directly beneath the destination's parent.", nameof(sourcePath));
        }

        if (OperatingSystem.IsWindows())
        {
            File.Move(sourcePath, destinationPath, overwrite: false);
            return;
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Atomic create-only file publication is not supported on this platform.");
        }

        // Unix File.Move can race through a replacing rename; link creates the destination without replacement.
        if (LinkUnix(sourcePath, destinationPath) == 0)
        {
            return;
        }

        int error = Marshal.GetLastPInvokeError();
        Win32Exception cause = new(error);
        string message = $"Could not publish staged file '{sourcePath}' to '{destinationPath}' without replacement: {cause.Message}";
        if (error is 1 or 13)
        {
            throw new UnauthorizedAccessException(message, cause);
        }

        throw new IOException(message, cause);
    }

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

    public void CreateNewDirectory(string path)
    {
        if (!TryCreateNewDirectory(path, privateDirectory: false))
        {
            throw new IOException($"Directory destination '{path}' already exists.");
        }
    }

    public string CreateStagingDirectory(string parentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string path = Path.Combine(parentDirectory, ".func-prepared-" + Path.GetRandomFileName());
            if (TryCreateNewDirectory(path, privateDirectory: true))
            {
                return path;
            }
        }

        throw new IOException($"Could not reserve a new staging directory under '{parentDirectory}'.");
    }

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

    private static bool TryCreateNewDirectory(string path, bool privateDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0'))
        {
            throw new ArgumentException("A directory path cannot contain a null character.", nameof(path));
        }

        bool windows = OperatingSystem.IsWindows();
        bool created = windows
            ? CreateDirectoryWindows(path, 0)
            : CreateDirectoryUnix(path, privateDirectory ? 0x1c0u : 0x1ffu) == 0;
        if (created)
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        if (windows ? error is 80 or 183 : error == 17)
        {
            return false;
        }

        Win32Exception cause = new(error);
        string message = $"Could not create directory '{path}': {cause.Message}";
        if (windows ? error == 5 : error is 1 or 13)
        {
            throw new UnauthorizedAccessException(message, cause);
        }

        if (windows ? error is 2 or 3 : error == 2)
        {
            throw new DirectoryNotFoundException(message, cause);
        }

        throw new IOException(message, cause);
    }

    // Directory.CreateDirectory succeeds for existing paths; publication and staging require exclusive ownership.
    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryWindows(string path, nint securityAttributes);

    [LibraryImport("libc", EntryPoint = "mkdir", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int CreateDirectoryUnix(string path, uint mode);

    [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static partial int LinkUnix(string sourcePath, string destinationPath);

    private static void EnsureParentDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
