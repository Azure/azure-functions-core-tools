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
    /// Creates a new, uniquely-named temporary directory under the system temp
    /// path and returns its full path.
    /// </summary>
    public string CreateTempDirectory();

    /// <summary>
    /// Creates a new, uniquely-named temporary directory on the same volume as
    /// <paramref name="siblingPath"/> and returns its path. When
    /// <paramref name="siblingPath"/> is an existing directory, the temp dir is
    /// created inside it; when it is a file path, the temp dir is created
    /// alongside the file.
    /// </summary>
    public string CreateTempDirectory(string siblingPath);

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
