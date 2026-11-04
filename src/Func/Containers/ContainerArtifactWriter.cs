// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Containers;

internal sealed class ContainerArtifactWriter(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <summary>
    /// Validates the complete artifact set before writing it.
    /// </summary>
    /// <exception cref="ContainerArtifactGenerationException">
    /// An artifact path is invalid, duplicated, or conflicts with an existing file without <paramref name="force"/>.
    /// </exception>
    public async Task WriteAsync(
        WorkingDirectory projectRoot,
        IReadOnlyList<ContainerArtifact> artifacts,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(artifacts);

        IReadOnlyList<(ContainerArtifact Artifact, string FullPath)> resolved = ResolvePaths(projectRoot, artifacts);
        string[] conflicts = [.. resolved
            .Where(item => _fileSystem.FileExists(item.FullPath))
            .Select(item => item.Artifact.RelativePath)];

        if (conflicts.Length > 0 && !force)
        {
            throw new ContainerArtifactGenerationException(
                $"Container artifacts were not generated because the following files already exist: {string.Join(", ", conflicts)}. "
                + "Use --force to replace these generated files.");
        }

        foreach ((ContainerArtifact artifact, string fullPath) in resolved)
        {
            await _fileSystem.WriteAllTextAsync(fullPath, artifact.Content, cancellationToken);
        }
    }

    private static IReadOnlyList<(ContainerArtifact Artifact, string FullPath)> ResolvePaths(
        WorkingDirectory projectRoot,
        IReadOnlyList<ContainerArtifact> artifacts)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot.Info.FullName));
        string rootPrefix = root + Path.DirectorySeparatorChar;
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        StringComparer pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        HashSet<string> paths = new(pathComparer);
        List<(ContainerArtifact Artifact, string FullPath)> resolved = [];

        foreach (ContainerArtifact artifact in artifacts)
        {
            if (Path.IsPathRooted(artifact.RelativePath))
            {
                throw new ContainerArtifactGenerationException(
                    $"Container artifact path '{artifact.RelativePath}' must be relative to the project root.");
            }

            string fullPath = Path.GetFullPath(Path.Combine(root, artifact.RelativePath));
            if (!fullPath.StartsWith(rootPrefix, pathComparison))
            {
                throw new ContainerArtifactGenerationException(
                    $"Container artifact path '{artifact.RelativePath}' escapes the project root.");
            }

            if (!paths.Add(fullPath))
            {
                throw new ContainerArtifactGenerationException(
                    $"Container artifact path '{artifact.RelativePath}' was returned more than once.");
            }

            resolved.Add((artifact, fullPath));
        }

        return resolved;
    }
}
