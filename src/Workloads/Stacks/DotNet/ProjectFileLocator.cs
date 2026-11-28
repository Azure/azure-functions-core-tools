// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Locates MSBuild project files using the physical filesystem.
/// </summary>
internal sealed class ProjectFileLocator : IProjectFileLocator
{
    /// <inheritdoc />
    public IReadOnlyList<string> FindProjectFilesAtOrAbove(string startDirectory, IReadOnlySet<string> extensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startDirectory);
        ArgumentNullException.ThrowIfNull(extensions);

        for (DirectoryInfo? directory = new(Path.GetFullPath(startDirectory));
            directory is not null;
            directory = directory.Parent)
        {
            if (!directory.Exists)
            {
                continue;
            }

            string[] projects = [.. directory.EnumerateFiles("*.*proj", SearchOption.TopDirectoryOnly)
                .Where(file => file.Extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                    && (extensions.Count == 0 || extensions.Contains(file.Extension)))
                .Select(file => file.FullName)
                .Order(StringComparer.Ordinal)];
            if (projects.Length > 0)
            {
                return projects;
            }
        }

        return [];
    }

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);
}
