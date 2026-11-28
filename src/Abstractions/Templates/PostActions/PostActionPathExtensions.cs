// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Path helpers for template post actions.
/// </summary>
internal static class PostActionPathExtensions
{
    extension(Path)
    {
        /// <summary>
        /// Resolves a path and ensures it remains within the specified directory.
        /// </summary>
        /// <param name="path">The path to resolve.</param>
        /// <param name="directoryPath">The directory that must contain the resolved path.</param>
        /// <returns>The absolute path when it is within the specified directory.</returns>
        /// <exception cref="ArgumentException">The path or directory path is empty or whitespace.</exception>
        /// <exception cref="InvalidOperationException">The resolved path is outside the specified directory.</exception>
        internal static string GetFullPathWithinDirectory(string path, string directoryPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

            string fullDirectoryPath = Path.GetFullPath(directoryPath);
            string fullPath = Path.GetFullPath(path, fullDirectoryPath);
            string relativePath = Path.GetRelativePath(fullDirectoryPath, fullPath);
            if (Path.IsPathRooted(relativePath)
                || relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Path '{path}' resolves outside directory '{fullDirectoryPath}'.");
            }

            return fullPath;
        }
    }
}
