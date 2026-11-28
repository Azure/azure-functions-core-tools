// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Locates MSBuild project files on disk.
/// </summary>
internal interface IProjectFileLocator
{
    /// <summary>
    /// Finds <c>*.*proj</c> files in the nearest directory containing them, starting at
    /// <paramref name="startDirectory"/> and walking up.
    /// </summary>
    /// <param name="startDirectory">The absolute directory to start searching from.</param>
    /// <param name="extensions">Allowed extensions including the leading dot, or empty to allow any.</param>
    /// <returns>Absolute project paths from a single directory, sorted ordinally; empty when none are found.</returns>
    public IReadOnlyList<string> FindProjectFilesAtOrAbove(string startDirectory, IReadOnlySet<string> extensions);

    /// <summary>
    /// Determines whether a file exists.
    /// </summary>
    /// <param name="path">The absolute file path.</param>
    /// <returns>True when the file exists; otherwise, false.</returns>
    public bool FileExists(string path);
}
