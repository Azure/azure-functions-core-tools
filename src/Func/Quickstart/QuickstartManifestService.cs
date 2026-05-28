// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches the quickstart manifest from the CDN with ETag-based caching
/// and stale-cache fallback on network failures.
/// </summary>
internal sealed class QuickstartManifestService(
    IHttpClientFactory httpClientFactory,
    IManifestCache cache,
    IOptions<QuickstartManifestOptions> options,
    TimeProvider timeProvider,
    ILogger<QuickstartManifestService> logger) : IQuickstartManifestService
{
    private const string GitRefPrefix = "v";
    private const string HttpSchemePrefix = "http";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IManifestCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly QuickstartManifestOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<QuickstartManifestService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<QuickstartManifest> GetManifestAsync(CancellationToken cancellationToken = default)
    {
        _cache.EnsureDirectory();

        if (TryGetLocalFilePath(_options.ManifestUrl, out string? localPath) && localPath is not null)
        {
            return await LoadFromLocalFileAsync(localPath, cancellationToken);
        }

        ManifestCacheMeta? cachedMeta = _cache.TryReadMeta();
        if (cachedMeta is not null && IsCacheFresh(cachedMeta))
        {
            _logger.LogDebug("Quickstart manifest cache is fresh; using cached copy.");
            return LoadCachedManifest();
        }

        // Try CDN with ETag conditional request.
        try
        {
            QuickstartManifest? result = await TryFetchFromCdnAsync(cachedMeta?.ETag, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "CDN fetch failed for quickstart manifest.");
        }

        // Fallback: return stale cache if available.
        QuickstartManifest? cached = TryLoadCachedManifest();
        if (cached is not null)
        {
            _logger.LogWarning("Using stale cached quickstart manifest.");
            return cached;
        }

        throw new InvalidOperationException(
            "Unable to fetch the quickstart manifest and no cached copy is available. " +
            "Check your network connection and try again.");
    }

    private async Task<QuickstartManifest?> TryFetchFromCdnAsync(string? etag, CancellationToken cancellationToken)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(QuickstartRegistration.HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, _options.ManifestUrl);
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogDebug("Manifest not modified (304); refreshing cache timestamp.");
            _cache.WriteMeta(new ManifestCacheMeta(etag ?? string.Empty, _timeProvider.GetUtcNow()));
            return LoadCachedManifest();
        }

        response.EnsureSuccessStatusCode();

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        string? newEtag = response.Headers.ETag?.Tag;

        List<QuickstartEntry>? entries = DeserializeEntries(content);
        if (entries is null)
        {
            return null;
        }

        _cache.WriteManifest(content);
        _cache.WriteMeta(new ManifestCacheMeta(newEtag ?? string.Empty, _timeProvider.GetUtcNow()));
        return BuildManifest(entries);
    }

    private static bool TryGetLocalFilePath(string url, out string? localPath)
    {
        localPath = null;

        // Support file:// URIs (but not UNC file URIs like file://server/share)
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile && !uri.IsUnc)
        {
            localPath = uri.LocalPath;
            return true;
        }

        // Support raw absolute file paths: drive-letter (C:\...) or Unix root (/).
        // UNC paths (\\server\share) are not accepted; use file:// URI instead.
        // Path.IsPathFullyQualified is OS-specific (won't recognise "C:\..." on Linux),
        // so we check for both Unix and Windows roots on every platform.
        if (!url.StartsWith(HttpSchemePrefix, StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("//", StringComparison.Ordinal)
            && !url.StartsWith(@"\\", StringComparison.Ordinal)
            && IsAbsolutePath(url))
        {
            localPath = url;
            return true;
        }

        return false;
    }

    private async Task<QuickstartManifest> LoadFromLocalFileAsync(string path, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading quickstart manifest from local file '{Path}'.", path);

        string? json = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
        if (json is null)
        {
            throw new FileNotFoundException(
                $"Manifest override file '{path}' does not exist. " +
                $"Check the {QuickstartRegistration.ManifestUrlEnvVar} environment variable.",
                path);
        }

        string errorMessage = $"Manifest override file '{path}' is empty or malformed.";

        List<QuickstartEntry>? entries;
        try
        {
            entries = DeserializeEntries(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(errorMessage, ex);
        }

        return entries is not null
            ? BuildManifest(entries)
            : throw new InvalidOperationException(errorMessage);
    }

    private QuickstartManifest BuildManifest(List<QuickstartEntry> entries)
    {
        List<QuickstartEntry> filtered = [];
        foreach (QuickstartEntry entry in entries)
        {
            (string Identifier, List<string> MissingFields)? missingInfo = GetMissingFields(entry);
            if (missingInfo is not null)
            {
                _logger.LogDebug("Dropping quickstart entry '{Identifier}': missing {Fields}.",
                    missingInfo.Value.Identifier, string.Join(", ", missingInfo.Value.MissingFields));
                continue;
            }

            if (!HasValidGitRef(entry))
            {
                continue;
            }

            if (!QuickstartUrlValidator.IsAllowed(entry.RepositoryUrl))
            {
                _logger.LogDebug(
                    "Dropping quickstart entry '{Id}': repository URL not allowed.", entry.Id);
                continue;
            }

            filtered.Add(entry);
        }

        return new QuickstartManifest(filtered);
    }

    private static (string Identifier, List<string> MissingFields)? GetMissingFields(QuickstartEntry entry)
    {
        List<string> missing = [];
        if (string.IsNullOrWhiteSpace(entry.Id)) missing.Add(nameof(entry.Id));
        if (string.IsNullOrWhiteSpace(entry.Language)) missing.Add(nameof(entry.Language));
        if (string.IsNullOrWhiteSpace(entry.Resource)) missing.Add(nameof(entry.Resource));
        if (string.IsNullOrWhiteSpace(entry.RepositoryUrl)) missing.Add(nameof(entry.RepositoryUrl));
        if (string.IsNullOrWhiteSpace(entry.FolderPath)) missing.Add(nameof(entry.FolderPath));
        if (string.IsNullOrWhiteSpace(entry.GitRef)) missing.Add(nameof(entry.GitRef));

        if (missing.Count == 0)
        {
            return null;
        }

        string identifier = !string.IsNullOrWhiteSpace(entry.Id) ? entry.Id
            : !string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.DisplayName
            : !string.IsNullOrWhiteSpace(entry.RepositoryUrl) ? entry.RepositoryUrl
            : "(unknown)";

        return (identifier, missing);
    }

    private static bool HasValidGitRef(QuickstartEntry entry)
    {
        return entry.GitRef is not null
            && entry.GitRef.StartsWith(GitRefPrefix, StringComparison.Ordinal);
    }

    private static List<QuickstartEntry>? DeserializeEntries(string json)
    {
        QuickstartManifestEnvelope? envelope = JsonSerializer.Deserialize(
            json, QuickstartJsonContext.Default.QuickstartManifestEnvelope);

        return envelope?.Templates;
    }

    private bool IsCacheFresh(ManifestCacheMeta meta) =>
        _timeProvider.GetUtcNow() - meta.CachedAt < _options.CacheTtl
        && _cache.ManifestExists();

    private QuickstartManifest LoadCachedManifest() =>
        TryLoadCachedManifest()
        ?? throw new InvalidOperationException("Cached manifest file is missing or corrupt.");

    private QuickstartManifest? TryLoadCachedManifest()
    {
        try
        {
            string? json = _cache.TryReadManifest();
            if (json is null)
            {
                return null;
            }

            List<QuickstartEntry>? entries = DeserializeEntries(json);
            return entries is null ? null : BuildManifest(entries);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to read cached quickstart manifest.");
            return null;
        }
    }

    /// <summary>
    /// OS-agnostic check for absolute paths. <see cref="Path.IsPathFullyQualified"/>
    /// is platform-specific and won't recognise Windows drive-letter paths on Linux.
    /// </summary>
    private static bool IsAbsolutePath(string path)
    {
        // Unix absolute
        if (path.StartsWith('/'))
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
}
