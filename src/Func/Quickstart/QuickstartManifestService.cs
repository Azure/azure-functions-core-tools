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
    ILogger<QuickstartManifestService> logger) : IQuickstartManifestService, IQuickstartCatalogSource
{
    private const string HttpSchemePrefix = "http";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IManifestCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly QuickstartManifestOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<QuickstartManifestService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public async Task<QuickstartManifest> GetManifestAsync(CancellationToken cancellationToken = default) =>
        (await GetCatalogDocumentAsync(cancellationToken)).Manifest;

    /// <inheritdoc/>
    public async Task<QuickstartCatalogDocument> GetCatalogDocumentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cache.EnsureDirectory();

        if (TryGetLocalFilePath(_options.ManifestUrl, out string? localPath) && localPath is not null)
        {
            return await LoadFromLocalFileAsync(localPath, cancellationToken);
        }

        ManifestCacheMeta? cachedMeta = _cache.TryReadMeta();
        if (cachedMeta is not null && IsCacheFresh(cachedMeta))
        {
            _logger.LogDebug("Quickstart manifest cache is fresh; using cached copy.");
            return LoadCachedManifest(cachedMeta, QuickstartCatalogOrigin.FreshCache);
        }

        // Try CDN with ETag conditional request.
        try
        {
            QuickstartCatalogDocument? result = await TryFetchFromCdnAsync(cachedMeta, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "CDN fetch failed for quickstart manifest.");
        }

        // Fallback: return stale cache if available.
        cancellationToken.ThrowIfCancellationRequested();
        QuickstartCatalogDocument? cached = TryLoadCachedManifest(cachedMeta, QuickstartCatalogOrigin.StaleCache);
        if (cached is not null)
        {
            _logger.LogWarning("Using stale cached quickstart manifest.");
            return cached;
        }

        throw new InvalidOperationException(
            "Unable to fetch the quickstart manifest and no cached copy is available. " +
            "Check your network connection and try again.");
    }

    private async Task<QuickstartCatalogDocument?> TryFetchFromCdnAsync(ManifestCacheMeta? cachedMeta, CancellationToken cancellationToken)
    {
        HttpClient httpClient = _httpClientFactory.CreateClient(QuickstartRegistration.HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, _options.ManifestUrl);
        string? etag = cachedMeta?.SourceUrl == _options.ManifestUrl ? cachedMeta.ETag : null;
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (string.IsNullOrEmpty(etag))
            {
                _logger.LogWarning("Manifest returned 304 without a source-matched conditional request; cache was not revalidated.");
                return null;
            }

            _logger.LogDebug("Manifest not modified (304); refreshing cache timestamp.");
            ManifestCacheMeta refreshed = new(etag, _timeProvider.GetUtcNow(), cachedMeta?.SourceUrl);
            _cache.WriteMeta(refreshed);
            return LoadCachedManifest(refreshed, QuickstartCatalogOrigin.RevalidatedCache);
        }

        response.EnsureSuccessStatusCode();

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        string? newEtag = response.Headers.ETag?.Tag;

        List<QuickstartManifestItem>? entries = DeserializeEntries(content);
        if (entries is null)
        {
            return null;
        }

        ManifestCacheMeta downloaded = new(newEtag ?? string.Empty, _timeProvider.GetUtcNow(), _options.ManifestUrl);
        _cache.WriteManifest(content);
        _cache.WriteMeta(downloaded);
        return BuildManifest(entries, new(QuickstartCatalogOrigin.Download, downloaded.SourceUrl, downloaded.CachedAt));
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

    private async Task<QuickstartCatalogDocument> LoadFromLocalFileAsync(string path, CancellationToken cancellationToken)
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

        List<QuickstartManifestItem>? entries;
        try
        {
            entries = DeserializeEntries(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(errorMessage, ex);
        }

        return entries is not null
            ? BuildManifest(entries, new(QuickstartCatalogOrigin.LocalFile, _options.ManifestUrl, null))
            : throw new InvalidOperationException(errorMessage);
    }

    private QuickstartCatalogDocument BuildManifest(List<QuickstartManifestItem> entries, QuickstartCatalogProvenance provenance)
    {
        List<QuickstartManifestItem> filtered = [];
        foreach (QuickstartManifestItem item in entries)
        {
            QuickstartEntry entry = item.Entry;
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

            filtered.Add(item);
        }

        return new(new QuickstartManifest(filtered.Select(item => item.Entry).ToList()),
            filtered.OrderBy(item => item.Entry.Priority).ToList().AsReadOnly(), provenance);
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
            && entry.GitRef.StartsWith(QuickstartConstants.TagRefPrefix, StringComparison.Ordinal);
    }

    private static List<QuickstartManifestItem>? DeserializeEntries(string json)
    {
        QuickstartManifestEnvelope? envelope = JsonSerializer.Deserialize(
            json, QuickstartJsonContext.Default.QuickstartManifestEnvelope);

        if (envelope?.Templates is null)
        {
            return null;
        }

        List<QuickstartManifestItem> items = [];
        foreach (JsonElement element in envelope.Templates)
        {
            QuickstartEntry entry = element.Deserialize(QuickstartJsonContext.Default.QuickstartEntry)
                ?? throw new JsonException("A quickstart manifest entry must be an object.");
            JsonElement metadata = default;
            string? diagnostic = null;
            bool found = false;
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "packageReference", StringComparison.OrdinalIgnoreCase))
                {
                    if (found)
                    {
                        diagnostic = "The packageReference property is duplicated.";
                    }

                    found = true;
                    metadata = property.Value;
                }
            }

            items.Add(new(entry, metadata, diagnostic));
        }

        return items;
    }

    private bool IsCacheFresh(ManifestCacheMeta meta) =>
        _timeProvider.GetUtcNow() - meta.CachedAt < _options.CacheTtl
        && meta.SourceUrl == _options.ManifestUrl
        && _cache.ManifestExists();

    private QuickstartCatalogDocument LoadCachedManifest(ManifestCacheMeta? meta, QuickstartCatalogOrigin origin) =>
        TryLoadCachedManifest(meta, origin)
        ?? throw new InvalidOperationException("Cached manifest file is missing or corrupt.");

    private QuickstartCatalogDocument? TryLoadCachedManifest(ManifestCacheMeta? meta, QuickstartCatalogOrigin origin)
    {
        try
        {
            string? json = _cache.TryReadManifest();
            if (json is null)
            {
                return null;
            }

            List<QuickstartManifestItem>? entries = DeserializeEntries(json);
            return entries is null ? null : BuildManifest(entries, new(origin, meta?.SourceUrl, meta?.CachedAt));
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
