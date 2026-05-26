// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches a quickstart template into the target directory, either by
/// downloading the GitHub archive zip (http) or by shallow-cloning with git.
/// </summary>
internal class QuickstartScaffolder : IQuickstartScaffolder
{
    private readonly HttpClient _httpClient;
    private readonly IGitRunner _gitRunner;
    private readonly ILogger<QuickstartScaffolder> _logger;

    public QuickstartScaffolder(
        HttpClient httpClient,
        IGitRunner gitRunner,
        ILogger<QuickstartScaffolder> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _gitRunner = gitRunner ?? throw new ArgumentNullException(nameof(gitRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task ScaffoldAsync(
        QuickstartEntry entry,
        string targetPath,
        FetchMode fetchMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        if (!QuickstartUrlValidator.IsAllowed(entry.RepositoryUrl))
        {
            throw new InvalidOperationException(
                $"Repository URL '{entry.RepositoryUrl}' is not allowed. " +
                "Only HTTPS URLs on github.com from trusted GitHub organizations are supported.");
        }

        if (Directory.Exists(targetPath) &&
            Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            throw new InvalidOperationException(
                $"Target directory '{targetPath}' already exists and is not empty. " +
                "Use a different path or remove the existing contents.");
        }

        FetchMode resolvedMode = await ResolveFetchModeAsync(fetchMode, cancellationToken);

        Directory.CreateDirectory(targetPath);

        if (resolvedMode == FetchMode.Git)
        {
            await FetchWithGitAsync(entry, targetPath, cancellationToken);
        }
        else
        {
            await FetchWithHttpAsync(entry, targetPath, cancellationToken);
        }

        RemoveRepositoryMetadata(targetPath);
    }

    private async Task<FetchMode> ResolveFetchModeAsync(FetchMode requested, CancellationToken ct)
    {
        switch (requested)
        {
            case FetchMode.Git:
                if (!await _gitRunner.IsAvailableAsync(ct))
                {
                    throw new InvalidOperationException(
                        "git 2.25 or later was not found on PATH. Install a recent git or use '--fetch http' to download a zip archive instead.");
                }

                return FetchMode.Git;

            case FetchMode.Http:
                return FetchMode.Http;

            case FetchMode.Auto:
                if (await _gitRunner.IsAvailableAsync(ct))
                {
                    _logger.LogDebug("git 2.25+ available; using git to fetch the quickstart.");
                    return FetchMode.Git;
                }

                _logger.LogDebug("git 2.25+ not available; falling back to http zip fetch.");
                return FetchMode.Http;

            default:
                // Defensive: a future FetchMode value added to the enum must update
                // this switch. Fall-through silently would mask the bug.
                throw new ArgumentOutOfRangeException(
                    nameof(requested), requested, $"Unsupported FetchMode value: {requested}");
        }
    }

    private async Task FetchWithGitAsync(
        QuickstartEntry entry,
        string targetPath,
        CancellationToken cancellationToken)
    {
        // Pass null when no ref is pinned so GitRunner clones the remote's
        // default branch instead of trying to resolve the literal "HEAD".
        string? gitRef = string.IsNullOrWhiteSpace(entry.GitRef) ? null : entry.GitRef;

        _logger.LogDebug(
            "Shallow-cloning {Repo} at {Ref} into {Target} (folderPath={FolderPath})",
            entry.RepositoryUrl, gitRef ?? "<default branch>", targetPath, entry.FolderPath);

        GitCloneResult result = await _gitRunner.ShallowCloneAsync(
            entry.RepositoryUrl, gitRef, targetPath, entry.FolderPath, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git clone failed (exit code {result.ExitCode}): {result.Stderr.Trim()}");
        }

        // When sparse-checkout filtered to a subfolder, the content lives at
        // <target>/<folderPath>/*. Promote it to <target>/* to match http behaviour.
        if (!string.IsNullOrWhiteSpace(entry.FolderPath) && entry.FolderPath != ".")
        {
            PromoteFolder(targetPath, entry.FolderPath);
        }
    }

    private async Task FetchWithHttpAsync(
        QuickstartEntry entry,
        string targetPath,
        CancellationToken cancellationToken)
    {
        Uri zipUrl = BuildZipUrl(entry.RepositoryUrl, entry.GitRef);
        _logger.LogDebug("Downloading quickstart zip from {ZipUrl}", zipUrl);

        await using Stream zipStream = await DownloadZipAsync(zipUrl, cancellationToken);
        ExtractZip(zipStream, targetPath, entry.FolderPath, entry.RepositoryUrl);
    }

    private static Uri BuildZipUrl(string repositoryUrl, string? gitRef)
    {
        string trimmed = repositoryUrl.TrimEnd('/');
        string @ref = string.IsNullOrWhiteSpace(gitRef) ? "HEAD" : gitRef;
        return new Uri($"{trimmed}/archive/{@ref}.zip");
    }

    /// <summary>
    /// Virtual seam so tests can supply an in-memory zip stream without HTTP.
    /// </summary>
    protected virtual async Task<Stream> DownloadZipAsync(
        Uri zipUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await _httpClient.GetAsync(zipUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Copy to memory so the response can be disposed before extraction.
        MemoryStream ms = new();
        await response.Content.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;
        return ms;
    }

    private static void PromoteFolder(string targetPath, string folderPath)
    {
        string source = Path.GetFullPath(
            Path.Combine(targetPath, folderPath.Replace('\\', Path.DirectorySeparatorChar)));
        string fullTarget = Path.GetFullPath(targetPath);

        // Defence-in-depth: never let folderPath escape the target directory.
        string fullTargetWithSep =
            fullTarget.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!(source + Path.DirectorySeparatorChar).StartsWith(
                fullTargetWithSep, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"folderPath '{folderPath}' resolves outside the target directory.");
        }

        if (!Directory.Exists(source))
        {
            throw new InvalidOperationException(
                $"folderPath '{folderPath}' does not exist in the cloned repository.");
        }

        // Move everything from <target>/<folderPath>/ up to <target>/, then delete
        // the now-empty source. Use a temp staging name to avoid name collisions
        // (e.g. folderPath="foo" and the repo also has a top-level "foo" directory).
        string staging = Path.Combine(Path.GetDirectoryName(fullTarget)!,
            Path.GetFileName(fullTarget) + "-staging-" + Guid.NewGuid().ToString("N"));
        Directory.Move(source, staging);

        // Clean the original target (except staging which is outside it) and move staged content back.
        foreach (string dir in Directory.GetDirectories(fullTarget))
        {
            Directory.Delete(dir, recursive: true);
        }

        foreach (string file in Directory.GetFiles(fullTarget))
        {
            File.Delete(file);
        }

        foreach (string entry in Directory.GetFileSystemEntries(staging))
        {
            string name = Path.GetFileName(entry);
            string dest = Path.Combine(fullTarget, name);
            if (Directory.Exists(entry))
            {
                Directory.Move(entry, dest);
            }
            else
            {
                File.Move(entry, dest);
            }
        }

        Directory.Delete(staging, recursive: true);
    }

    private static void RemoveRepositoryMetadata(string targetPath)
    {
        // .git/ and .github/ belong to the source repository, not the user's
        // freshly-scaffolded project. Remove both regardless of how we fetched.
        foreach (string name in (string[])[".git", ".github"])
        {
            string path = Path.Combine(targetPath, name);
            if (Directory.Exists(path))
            {
                // .git/objects/pack/*.idx is marked read-only by git on Windows.
                MakeWritable(path);
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private static void MakeWritable(string root)
    {
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            FileAttributes attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }

    private static void ExtractZip(
        Stream zipStream,
        string targetPath,
        string folderPath,
        string repositoryUrl)
    {
        // GitHub archive zips have a top-level directory named "{repo}-{commitSha}/".
        // We detect it from the first entry rather than hardcoding the suffix.
        string repoName = ExtractRepoName(repositoryUrl);

        // Normalise folderPath to a forward-slash prefix (empty means ".")
        string folderPrefix = string.IsNullOrWhiteSpace(folderPath) || folderPath == "."
            ? string.Empty
            : folderPath.Replace('\\', '/').TrimEnd('/') + "/";

        using ZipArchive archive = new(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        // Resolve once so every entry comparison is against a stable absolute path.
        string fullTargetPath = Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        // Detect the actual top-level prefix from the first entry (e.g. "{repo}-{sha}/").
        // Fall back to "{repo}-HEAD/" if the archive is unexpectedly empty.
        string topLevelPrefix = archive.Entries.Count > 0
            ? archive.Entries[0].FullName
            : $"{repoName}-HEAD/";

        // Ensure prefix ends with "/" (the first entry is the root directory entry).
        if (!topLevelPrefix.EndsWith('/'))
        {
            topLevelPrefix = topLevelPrefix.Split('/')[0] + "/";
        }

        int filesExtracted = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string fullName = entry.FullName;

            // Strip the top-level prefix.
            if (!fullName.StartsWith(topLevelPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relativeName = fullName[topLevelPrefix.Length..];

            // Apply folderPath filter.
            if (!string.IsNullOrEmpty(folderPrefix))
            {
                if (!relativeName.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                relativeName = relativeName[folderPrefix.Length..];
            }

            // Skip empty names (directory entries).
            if (string.IsNullOrEmpty(relativeName))
            {
                continue;
            }

            string destinationPath = Path.Combine(
                targetPath,
                relativeName.Replace('/', Path.DirectorySeparatorChar));

            // Guard against zip entries that escape the target directory (path traversal).
            string fullDestPath = Path.GetFullPath(destinationPath);
            if (!fullDestPath.StartsWith(fullTargetPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Zip entry '{entry.FullName}' would be extracted outside the target directory. " +
                    "The archive may be malicious.");
            }

            // Create parent directories as needed.
            string? parentDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // Directory entries in zip have a trailing slash and no content.
            if (entry.Name == string.Empty)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            entry.ExtractToFile(destinationPath, overwrite: false);
            filesExtracted++;
        }

        if (filesExtracted == 0)
        {
            // An empty archive or one that matched nothing under folderPath would
            // otherwise leave the user with an empty directory and an exit-0 success
            // message — surface it as a real failure so the manifest can be fixed.
            throw new InvalidOperationException(
                "The downloaded archive contained no extractable files. " +
                "The manifest entry may be misconfigured or the source repository may be empty.");
        }
    }

    private static string ExtractRepoName(string repositoryUrl)
    {
        string[] segments = repositoryUrl.TrimEnd('/').Split('/');
        return segments[^1];
    }
}
