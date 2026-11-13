// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Extensions for <see cref="Path"/>.
/// </summary>
internal static class PathExtensions
{
    extension(Path)
    {
        /// <summary>
        /// Normalizes a relative path and verifies it resolves inside the base directory.
        /// </summary>
        /// <param name="path">The relative path to normalize.</param>
        /// <param name="baseDirectory">The directory the path must resolve inside.</param>
        /// <returns>The normalized relative path.</returns>
        internal static string NormalizePath(string path, string baseDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

            string relativePath = path.Replace('\\', '/');
            if (Path.IsPathRooted(relativePath))
            {
                throw new ArgumentException("The path must be relative to the base directory.", nameof(path));
            }

            string basePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
            string resolvedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(basePath, relativePath)));
            string basePathPrefix = Path.EndsInDirectorySeparator(basePath) ? basePath : basePath + Path.DirectorySeparatorChar;
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!resolvedPath.StartsWith(basePathPrefix, comparison)
                || string.Equals(resolvedPath, basePath, comparison))
            {
                throw new ArgumentException("The path must resolve to a directory inside the base directory.", nameof(path));
            }

            return Path.GetRelativePath(basePath, resolvedPath).Replace('\\', '/');
        }

        /// <summary>
        /// Combines a base directory and relative path, ensuring the result remains inside the base directory.
        /// </summary>
        /// <param name="baseDirectory">The directory the path must resolve inside.</param>
        /// <param name="relativePath">The relative path to combine with the base directory.</param>
        /// <returns>The normalized absolute path.</returns>
        internal static string CombineNormalized(string baseDirectory, string relativePath)
        {
            string normalizedPath = Path.NormalizePath(relativePath, baseDirectory);
            return Path.GetFullPath(Path.Combine(baseDirectory, normalizedPath));
        }
    }
}
