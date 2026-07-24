// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// File-system seam for the update pipeline. Abstracted so the pipeline can
/// be unit-tested without touching real disk.
/// </summary>
internal interface IUpdateFileSystem
{
    /// <summary>
    /// Creates a new, uniquely-named temporary directory and returns its path.
    /// </summary>
    public string CreateTempDirectory();

    /// <summary>
    /// Creates a new, uniquely-named temporary directory on the same volume as
    /// <paramref name="siblingPath"/> and returns its path. When
    /// <paramref name="siblingPath"/> is an existing directory, the temp dir is
    /// created inside it; otherwise it is created alongside the file.
    /// </summary>
    public string CreateTempDirectory(string siblingPath);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="path"/> exists as a directory.
    /// </summary>
    public bool DirectoryExists(string path);

    /// <summary>
    /// Moves a directory from <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>.
    /// </summary>
    public void MoveDirectory(string sourcePath, string destinationPath);

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

    /// <summary>
    /// Renames <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.
    /// </summary>
    public void RenameFile(string sourcePath, string destinationPath);

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
    /// Returns <c>true</c> when <paramref name="path"/> exists as a file.
    /// </summary>
    public bool FileExists(string path);

    /// <summary>
    /// Streams <paramref name="content"/> to <paramref name="filePath"/>,
    /// creating or overwriting the file.
    /// </summary>
    public Task SaveStreamToFileAsync(string filePath, Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Extracts a ZIP archive at <paramref name="zipPath"/> into
    /// <paramref name="destinationDirectory"/>.
    /// </summary>
    public void ExtractZip(string zipPath, string destinationDirectory);
}
