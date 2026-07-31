// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// The narrow filesystem surface post-action handlers touch. Kept behind an
/// interface so handlers stay unit-testable without hitting a real disk.
/// </summary>
internal interface IFuncTemplateFileSystem
{
    /// <summary>
    /// Returns whether a file exists at <paramref name="path"/>.
    /// </summary>
    public bool FileExists(string path);

    /// <summary>
    /// Reads the entire text content of <paramref name="path"/>.
    /// </summary>
    public string ReadAllText(string path);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/>, creating
    /// the parent directory when needed and overwriting any existing file.
    /// </summary>
    public void WriteAllText(string path, string content);

    /// <summary>
    /// Appends <paramref name="content"/> to <paramref name="path"/> without
    /// rewriting the existing bytes, so the file's byte-order mark and existing
    /// line endings are left untouched. Creates the file when it does not exist.
    /// </summary>
    public void AppendAllText(string path, string content);

    /// <summary>
    /// Deletes the file at <paramref name="path"/> if it exists.
    /// </summary>
    public void DeleteFile(string path);

    /// <summary>
    /// Returns the files directly under <paramref name="directory"/> matching
    /// <paramref name="searchPattern"/>, or an empty list when the directory
    /// does not exist.
    /// </summary>
    public IReadOnlyList<string> EnumerateFiles(string directory, string searchPattern);
}
