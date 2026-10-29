// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Filesystem abstraction shared across the CLI. Wraps <see cref="System.IO"/>
/// calls so they can be substituted in tests.
/// </summary>
internal interface IFileSystem
{
    // ── File operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/> exists as a file.
    /// </summary>
    public bool FileExists(string path);

    /// <summary>
    /// Reads a text file if it exists; returns <c>null</c> when the file is absent.
    /// </summary>
    public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a file for asynchronous sequential reading; returns <c>null</c>
    /// when the file is absent.
    /// </summary>
    public Stream? OpenReadIfExists(string path);

    /// <summary>
    /// Reads attributes without following a final symbolic link; returns null only when the path is absent.
    /// </summary>
    public FileAttributes? GetAttributesIfExists(string path);

    /// <summary>
    /// Opens a new binary file for asynchronous writing, refusing an occupied path without creating parent directories.
    /// </summary>
    public Stream CreateNewFile(string path);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> atomically
    /// (via temp-file-and-rename), creating parent directories if needed.
    /// </summary>
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);

    /// <summary>
    /// Streams <paramref name="content"/> to <paramref name="filePath"/>,
    /// creating or overwriting the file.
    /// </summary>
    public Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Moves (renames) a file from <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>. When <paramref name="overwrite"/>
    /// is <c>true</c>, an existing file at the destination is replaced.
    /// </summary>
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>
    /// Atomically publishes a staged file only if the destination is absent.
    /// The staging directory must be directly beneath the destination's parent; the source may remain for caller-owned cleanup.
    /// </summary>
    public void PublishFileNoReplace(string sourcePath, string destinationPath);

    /// <summary>
    /// Copies a single file from <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>, overwriting if it exists.
    /// </summary>
    public void CopyFile(string sourcePath, string destinationPath);

    /// <summary>
    /// Deletes a single file.
    /// </summary>
    public void DeleteFile(string path);

    /// <summary>
    /// Deletes a file if it exists; does nothing when the file is absent.
    /// </summary>
    public Task DeleteFileIfExistsAsync(string path);

    // ── Directory operations ────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/> exists as a directory.
    /// </summary>
    public bool DirectoryExists(string path);

    /// <summary>
    /// Creates a directory (and any missing parents) at <paramref name="path"/>.
    /// </summary>
    public void CreateDirectory(string path);

    /// <summary>
    /// Creates exactly one new directory, refusing an occupied path without creating missing parents.
    /// </summary>
    public void CreateNewDirectory(string path);

    /// <summary>
    /// Exclusively creates an empty staging directory under an existing parent.
    /// The caller owns its path and must report cleanup failures.
    /// </summary>
    public string CreateStagingDirectory(string parentDirectory);

    /// <summary>
    /// Creates a new, uniquely-named temporary directory under the system temp
    /// path and returns it as an <see cref="IDisposable"/> handle.
    /// </summary>
    public TempDirectory CreateTempDirectory();

    /// <summary>
    /// Creates a new, uniquely-named temporary directory under
    /// <paramref name="parentDirectory"/> and returns it as an
    /// <see cref="IDisposable"/> handle.
    /// </summary>
    public TempDirectory CreateTempDirectory(string parentDirectory);

    /// <summary>
    /// Recursively copies all files and subdirectories from
    /// <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.
    /// </summary>
    public void CopyDirectory(string sourcePath, string destinationPath);

    /// <summary>
    /// Deletes a directory and all its contents.
    /// </summary>
    public void DeleteDirectory(string path);

    /// <summary>
    /// Returns the paths of all files inside <paramref name="directoryPath"/>,
    /// searching all subdirectories.
    /// </summary>
    public IReadOnlyList<string> GetFiles(string directoryPath);

    // ── Archive operations ──────────────────────────────────────────────────

    /// <summary>
    /// Extracts a ZIP archive at <paramref name="zipPath"/> into
    /// <paramref name="destinationDirectory"/>.
    /// </summary>
    public void ExtractZip(string zipPath, string destinationDirectory);

    /// <summary>
    /// Extracts a gzipped tar archive at <paramref name="tarGzPath"/> into
    /// <paramref name="destinationDirectory"/>.
    /// </summary>
    public void ExtractTarGz(string tarGzPath, string destinationDirectory);

    // ── Hash operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes the SHA-256 hash of a file and returns it as a lowercase hex string.
    /// </summary>
    public Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken);
}
