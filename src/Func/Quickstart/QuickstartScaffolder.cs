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

        string tempDir = Path.Combine(Path.GetTempPath(), $"{TempDirectoryPrefix}{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

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

        string subfolder = Path.Combine(tempDir, folderPath.Replace('/', Path.DirectorySeparatorChar));
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
        foreach (string dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
        }

        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDir, file);
            string dest = Path.Combine(targetDirectory, relative);
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

        if (entry.FolderPath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Invalid FolderPath '{entry.FolderPath}': must not contain '..' (path traversal).",
                nameof(entry));
        }

        if (Path.IsPathRooted(entry.FolderPath))
        {
            throw new ArgumentException(
                $"Invalid FolderPath '{entry.FolderPath}': must be a relative path.",
                nameof(entry));
        }
    }

    private static IReadOnlyDictionary<FetchMode, ITemplateFetcher> BuildFetcherMap(IEnumerable<ITemplateFetcher> fetchers)
    {
        ArgumentNullException.ThrowIfNull(fetchers);

        Dictionary<FetchMode, ITemplateFetcher> map = [];
        foreach (ITemplateFetcher fetcher in fetchers)
        {
            map[fetcher.Mode] = fetcher;
        }

        return map;
    }
}
