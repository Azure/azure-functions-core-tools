// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Orchestrates template scaffolding: validates the entry, delegates fetching
/// to the appropriate <see cref="ITemplateFetcher"/>, then cleans metadata,
/// promotes subfolders, and copies the result to the target directory.
/// </summary>
internal sealed class QuickstartScaffolder(
    IEnumerable<ITemplateFetcher> fetchers,
    IFetchModeResolver fetchModeResolver,
    ILogger<QuickstartScaffolder> logger) : IQuickstartScaffolder
{
    private const string TempDirectoryPrefix = "func-quickstart-";

    private readonly IReadOnlyDictionary<FetchMode, ITemplateFetcher> _fetchers = BuildFetcherMap(fetchers);
    private readonly IFetchModeResolver _fetchModeResolver = fetchModeResolver ?? throw new ArgumentNullException(nameof(fetchModeResolver));
    private readonly ILogger<QuickstartScaffolder> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task ScaffoldAsync(
        QuickstartEntry entry,
        string targetDirectory,
        FetchMode fetchMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        ValidateEntry(entry);

        DirectoryInfo tempDirInfo = Directory.CreateTempSubdirectory(TempDirectoryPrefix);
        string tempDir = tempDirInfo.FullName;

        try
        {
            FetchMode resolved = await _fetchModeResolver.ResolveAsync(fetchMode, cancellationToken);
            ITemplateFetcher fetcher = GetFetcher(resolved);

            await fetcher.FetchAsync(entry, tempDir, cancellationToken);

            RemoveTemplateMetadata(tempDir);
            string sourceDir = PromoteSubfolder(tempDir, entry.FolderPath);
            CopyContentsToTarget(sourceDir, targetDirectory);

            _logger.LogDebug("Scaffolded template {TemplateId} into {TargetDirectory}", entry.Id, targetDirectory);
        }
        finally
        {
            DirectoryGuard.TryDelete(new DirectoryInfo(tempDir));
        }
    }

    private ITemplateFetcher GetFetcher(FetchMode mode)
    {
        if (_fetchers.TryGetValue(mode, out ITemplateFetcher? fetcher))
        {
            return fetcher;
        }

        throw new InvalidOperationException($"No template fetcher registered for mode '{mode}'.");
    }

    private static void RemoveTemplateMetadata(string directory)
    {
        string gitDir = Path.Combine(directory, ".git");
        if (Directory.Exists(gitDir))
        {
            DirectoryGuard.ClearReadOnlyRecursive(new DirectoryInfo(gitDir));
            Directory.Delete(gitDir, recursive: true);
        }

        string githubDir = Path.Combine(directory, ".github");
        if (Directory.Exists(githubDir))
        {
            Directory.Delete(githubDir, recursive: true);
        }
    }

    private static string PromoteSubfolder(string tempDir, string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || folderPath == ".")
        {
            return tempDir;
        }

        if (IsAbsoluteOrTraversal(folderPath))
        {
            throw new ArgumentException(
                $"FolderPath '{folderPath}' resolves outside the template directory. " +
                "It must be a relative path within the template.",
                nameof(folderPath));
        }

        string subfolder = Path.GetFullPath(Path.Combine(tempDir, folderPath));
        string containingDir = Path.GetFullPath(tempDir + Path.DirectorySeparatorChar);

        if (!subfolder.StartsWith(containingDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"FolderPath '{folderPath}' resolves outside the template directory. " +
                "It must be a relative path within the template.",
                nameof(folderPath));
        }

        if (!Directory.Exists(subfolder))
        {
            throw new DirectoryNotFoundException(
                $"Subfolder '{folderPath}' does not exist in the downloaded template. " +
                "The template manifest may be out of date.");
        }

        return subfolder;
    }

    private static void CopyContentsToTarget(string sourceDir, string targetDirectory)
    {
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(targetDirectory, relative);
            string? destDir = Path.GetDirectoryName(dest);

            if (destDir is not null)
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(file, dest, overwrite: true);
        }
    }

    private static void ValidateEntry(QuickstartEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.GitRef))
        {
            throw new ArgumentException(
                $"Template '{entry.Id}' has no GitRef. A git tag or branch reference is required.",
                nameof(entry));
        }

        if (entry.GitRef.StartsWith('-'))
        {
            throw new ArgumentException(
                $"Invalid GitRef '{entry.GitRef}': must not start with '-' (potential flag injection).",
                nameof(entry));
        }

        if (IsAbsoluteOrTraversal(entry.FolderPath))
        {
            throw new ArgumentException(
                $"Invalid FolderPath '{entry.FolderPath}': must be a relative path without traversal.",
                nameof(entry));
        }
    }

    /// <summary>
    /// OS-agnostic check for absolute or traversal paths. Manifest data may contain
    /// Windows-style roots (<c>C:\foo</c>) even when the CLI runs on Linux/macOS,
    /// so we check for both Unix and Windows patterns on every platform.
    /// </summary>
    private static bool IsAbsoluteOrTraversal(string path)
    {
        if (path.Contains("..", StringComparison.Ordinal))
        {
            return true;
        }

        // Unix absolute or UNC path
        if (path.StartsWith('/') || path.StartsWith('\\'))
        {
            return true;
        }

        // Windows drive-letter root (e.g. "C:\", "D:/")
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<FetchMode, ITemplateFetcher> BuildFetcherMap(IEnumerable<ITemplateFetcher> fetchers)
    {
        ArgumentNullException.ThrowIfNull(fetchers);

        Dictionary<FetchMode, ITemplateFetcher> map = [];
        foreach (ITemplateFetcher fetcher in fetchers)
        {
            if (!map.TryAdd(fetcher.Mode, fetcher))
            {
                throw new InvalidOperationException(
                    $"Duplicate template fetcher registered for mode '{fetcher.Mode}'.");
            }
        }

        return map;
    }
}
