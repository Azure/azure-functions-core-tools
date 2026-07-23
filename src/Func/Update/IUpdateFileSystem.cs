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
    /// Returns <c>true</c> when <paramref name="path"/> exists as a directory.
    /// </summary>
    public bool DirectoryExists(string path);

    /// <summary>
    /// Moves a directory from <paramref name="sourcePath"/> to
    /// <paramref name="destinationPath"/>.
    /// </summary>
    public void MoveDirectory(string sourcePath, string destinationPath);

    /// <summary>
    /// Deletes a directory and all its contents.
    /// </summary>
    public void DeleteDirectory(string path);

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
