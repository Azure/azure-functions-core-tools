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
    /// <param name="path">The file path to check.</param>
    /// <returns><see langword="true"/> if the file exists; otherwise, <see langword="false"/>.</returns>
    public bool FileExists(string path);

    /// <summary>
    /// Reads a text file if it exists; returns <c>null</c> when the file is absent.
    /// </summary>
    /// <param name="path">The path of the text file to read.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>The file contents, or <see langword="null"/> if the file is absent.</returns>
    public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Opens a file for asynchronous sequential reading; returns <c>null</c>
    /// when the file is absent.
    /// </summary>
    /// <param name="path">The path of the file to open.</param>
    /// <returns>A readable stream, or <see langword="null"/> if the file is absent.</returns>
    public Stream? OpenReadIfExists(string path);

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="path"/> atomically
    /// (via temp-file-and-rename), creating parent directories if needed.
    /// </summary>
    /// <param name="path">The path of the file to write.</param>
    /// <param name="contents">The text to write.</param>
    /// <param name="cancellationToken">A token to observe while writing.</param>
    /// <returns>A task representing the write operation.</returns>
    public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken);

    /// <summary>
    /// Streams <paramref name="content"/> to <paramref name="filePath"/>,
    /// creating or overwriting the file.
    /// </summary>
    /// <param name="filePath">The destination file path.</param>
    /// <param name="content">The stream whose contents are written to the file.</param>
    /// <param name="cancellationToken">A token to observe while writing.</param>
    /// <returns>A task representing the save operation.</returns>
    public Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Moves (renames) a file from <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>. When <paramref name="overwrite"/>
    /// is <c>true</c>, an existing file at the destination is replaced.
    /// </summary>
    /// <param name="sourcePath">The path of the file to move.</param>
    /// <param name="destinationPath">The destination path.</param>
    /// <param name="overwrite">Whether to replace an existing destination file.</param>
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);

    /// <summary>
    /// Copies a single file from <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>, overwriting if it exists.
    /// </summary>
    /// <param name="sourcePath">The path of the file to copy.</param>
    /// <param name="destinationPath">The destination path.</param>
    public void CopyFile(string sourcePath, string destinationPath);

    /// <summary>
    /// Deletes a single file.
    /// </summary>
    /// <param name="path">The path of the file to delete.</param>
    public void DeleteFile(string path);

    /// <summary>
    /// Deletes a file if it exists; does nothing when the file is absent.
    /// </summary>
    /// <param name="path">The path of the file to delete.</param>
    /// <returns>A task representing the delete operation.</returns>
    public Task DeleteFileIfExistsAsync(string path);

    // ── Directory operations ────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/> exists as a directory.
    /// </summary>
    /// <param name="path">The directory path to check.</param>
    /// <returns><see langword="true"/> if the directory exists; otherwise, <see langword="false"/>.</returns>
    public bool DirectoryExists(string path);

    /// <summary>
    /// Creates a directory (and any missing parents) at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the directory to create.</param>
    public void CreateDirectory(string path);

    /// <summary>
    /// Creates a new, uniquely-named temporary directory under the system temp
    /// path and returns it as an <see cref="IDisposable"/> handle.
    /// </summary>
    /// <returns>A handle to the temporary directory.</returns>
    public TempDirectory CreateTempDirectory();

    /// <summary>
    /// Creates a new, uniquely-named temporary directory under
    /// <paramref name="parentDirectory"/> and returns it as an
    /// <see cref="IDisposable"/> handle.
    /// </summary>
    /// <param name="parentDirectory">The directory under which to create the temporary directory.</param>
    /// <returns>A handle to the temporary directory.</returns>
    public TempDirectory CreateTempDirectory(string parentDirectory);

    /// <summary>
    /// Recursively copies all files and subdirectories from
    /// <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.
    /// </summary>
    /// <param name="sourcePath">The directory to copy.</param>
    /// <param name="destinationPath">The destination directory.</param>
    public void CopyDirectory(string sourcePath, string destinationPath);

    /// <summary>
    /// Deletes a directory and all its contents.
    /// </summary>
    /// <param name="path">The path of the directory to delete.</param>
    public void DeleteDirectory(string path);

    /// <summary>
    /// Returns the paths of all files inside <paramref name="directoryPath"/>,
    /// searching all subdirectories.
    /// </summary>
    /// <param name="directoryPath">The directory whose files to enumerate.</param>
    /// <returns>The paths of all files in the directory tree.</returns>
    public IReadOnlyList<string> GetFiles(string directoryPath);

    /// <summary>
    /// Returns the UTC last write time of the file or directory at <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The path of the file or directory to inspect.</param>
    /// <returns>The last write time in UTC.</returns>
    public DateTime GetLastWriteTimeUtc(string path);

    // ── Archive operations ──────────────────────────────────────────────────

    /// <summary>
    /// Extracts a ZIP archive at <paramref name="zipPath"/> into
    /// <paramref name="destinationDirectory"/>.
    /// </summary>
    /// <param name="zipPath">The path of the ZIP archive to extract.</param>
    /// <param name="destinationDirectory">The directory into which to extract the archive.</param>
    public void ExtractZip(string zipPath, string destinationDirectory);

    /// <summary>
    /// Extracts a gzipped tar archive at <paramref name="tarGzPath"/> into
    /// <paramref name="destinationDirectory"/>.
    /// </summary>
    /// <param name="tarGzPath">The path of the gzipped tar archive to extract.</param>
    /// <param name="destinationDirectory">The directory into which to extract the archive.</param>
    public void ExtractTarGz(string tarGzPath, string destinationDirectory);

    // ── Hash operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Computes the SHA-256 hash of a file and returns it as a lowercase hex string.
    /// </summary>
    /// <param name="filePath">The path of the file to hash.</param>
    /// <param name="cancellationToken">A token to observe while computing the hash.</param>
    /// <returns>The SHA-256 hash as a lowercase hexadecimal string.</returns>
    public Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken);
}
