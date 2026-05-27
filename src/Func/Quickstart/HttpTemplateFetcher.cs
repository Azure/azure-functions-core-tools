// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Azure.Functions.Cli.Common;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches template content via HTTP zip archive download from GitHub.
/// </summary>
internal sealed class HttpTemplateFetcher(IHttpClientFactory httpClientFactory, ILogger<HttpTemplateFetcher> logger) : ITemplateFetcher
{
    /// <summary>
    /// Named HttpClient identifier used at both registration and resolution.
    /// </summary>
    internal const string HttpClientName = "QuickstartScaffolder";

    private const string ArchiveFileName = "archive.zip";
    private const string ExtractionDirectory = "extracted";
    private const string ArchiveUrlPathSegment = "/archive/refs/tags/";
    private const string ArchiveUrlExtension = ".zip";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly ILogger<HttpTemplateFetcher> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public FetchMode Mode => FetchMode.Http;

    /// <inheritdoc />
    public async Task FetchAsync(QuickstartEntry entry, string tempDirectory, CancellationToken cancellationToken)
    {
        string archiveUrl = BuildArchiveUrl(entry);
        _logger.LogDebug("Downloading archive from {Url}", archiveUrl);

        using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await client.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Tag '{entry.GitRef}' not found in repository '{entry.RepositoryUrl}'. " +
                "The template manifest may be out of date. Try running 'func quickstart list --refresh'.");
        }

        response.EnsureSuccessStatusCode();

        string zipPath = Path.Combine(tempDirectory, ArchiveFileName);
        await using (FileStream fs = File.Create(zipPath))
        {
            await response.Content.CopyToAsync(fs, cancellationToken);
        }

        string extractDir = Path.Combine(tempDirectory, ExtractionDirectory);

        // ZipFile.ExtractToDirectory has built-in path traversal protection
        // since .NET 6 (throws IOException on entries that escape the target).
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        UnwrapArchiveRoot(entry, tempDirectory, extractDir);

        File.Delete(zipPath);
        DirectoryGuard.TryDelete(new DirectoryInfo(extractDir));
    }

    private static void UnwrapArchiveRoot(QuickstartEntry entry, string tempDirectory, string extractDir)
    {
        // GitHub archives have a single root folder (e.g. "repo-name-tagname/")
        string[] topLevelDirs = Directory.GetDirectories(extractDir);
        if (topLevelDirs.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected a single root folder in the archive for '{entry.Id}', " +
                $"but found {topLevelDirs.Length}. The archive format may have changed.");
        }

        string archiveRoot = topLevelDirs[0];
        foreach (string dir in Directory.GetDirectories(archiveRoot))
        {
            string dest = Path.Combine(tempDirectory, Path.GetFileName(dir));
            Directory.Move(dir, dest);
        }

        foreach (string file in Directory.GetFiles(archiveRoot))
        {
            string dest = Path.Combine(tempDirectory, Path.GetFileName(file));
            File.Move(file, dest);
        }
    }

    private static string BuildArchiveUrl(QuickstartEntry entry)
    {
        var uri = new Uri(entry.RepositoryUrl);
        string path = uri.AbsolutePath.TrimEnd('/');

        string tag = entry.GitRef ?? throw new InvalidOperationException(
            $"Template '{entry.Id}' has no GitRef. Cannot build archive URL.");

        return $"{QuickstartConstants.GitHubHost}{path}{ArchiveUrlPathSegment}{tag}{ArchiveUrlExtension}";
    }
}
