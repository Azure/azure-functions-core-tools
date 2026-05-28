// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Pure filesystem helpers for directory state checks and cleanup.
/// Shared by commands that scaffold into a user-supplied directory
/// (init, quickstart).
/// </summary>
internal static class DirectoryGuard
{
    /// <summary>
    /// Returns <see langword="true"/> if the directory contains any file or
    /// any subdirectory other than <c>.git</c>.
    /// </summary>
    internal static bool HasNonGitContent(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return directory.EnumerateFiles().Any()
            || directory.EnumerateDirectories()
                .Any(d => !string.Equals(d.Name, ".git", StringComparison.Ordinal));
    }

    /// <summary>
    /// Deletes all files and subdirectories in <paramref name="directory"/>
    /// except <c>.git</c>. Clears read-only attributes before deletion.
    /// </summary>
    internal static void ClearExceptGit(DirectoryInfo directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        foreach (FileInfo file in directory.EnumerateFiles())
        {
            file.Attributes = FileAttributes.Normal;
            file.Delete();
        }

        foreach (DirectoryInfo dir in directory.EnumerateDirectories())
        {
            if (string.Equals(dir.Name, ".git", StringComparison.Ordinal))
            {
                continue;
            }

            ClearReadOnlyRecursive(dir);
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Clears read-only attributes from all files in a directory tree so
    /// the directory can be deleted. Useful for git clone artifacts.
    /// </summary>
    internal static void ClearReadOnlyRecursive(DirectoryInfo directory)
    {
        foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                file.Attributes = FileAttributes.Normal;
            }
        }
    }

    /// <summary>
    /// Attempts to delete a directory and all contents. Clears read-only
    /// attributes first. Swallows exceptions for best-effort cleanup.
    /// </summary>
    internal static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            if (directory.Exists)
            {
                ClearReadOnlyRecursive(directory);
                directory.Delete(recursive: true);
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup — caller decided this failure is acceptable.
        }
    }
}
