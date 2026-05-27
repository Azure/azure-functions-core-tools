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
        httpClient.Timeout = _options.HttpTimeout;
        using HttpRequestMessage request = new(HttpMethod.Get, _options.ManifestUrl);
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogDebug("Manifest not modified (304); refreshing cache timestamp.");
            _cache.WriteMeta(new ManifestCacheMeta(etag!, _timeProvider.GetUtcNow()));
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

        // Support file:// URIs
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            localPath = uri.LocalPath;
            return true;
        }

        // Support raw absolute file paths (Windows or Unix)
        if (Path.IsPathFullyQualified(url) && !url.StartsWith(HttpSchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            localPath = url;
            return true;
        }

        return false;
    }

    private async Task<QuickstartManifest> LoadFromLocalFileAsync(string path, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading quickstart manifest from local file '{Path}'.", path);

        string? json = await _cache.TryReadLocalFileAsync(path, cancellationToken);
        if (json is null)
        {
            throw new FileNotFoundException(
                $"Manifest override file '{path}' does not exist. " +
                $"Check the {QuickstartRegistration.ManifestUrlEnvVar} environment variable.",
                path);
        }

        List<QuickstartEntry>? entries = DeserializeEntries(json)
            ?? throw new InvalidOperationException(
                $"Manifest override file '{path}' is empty or malformed.");

        return BuildManifest(entries);
    }

    private QuickstartManifest BuildManifest(List<QuickstartEntry> entries)
    {
        List<QuickstartEntry> filtered = [];
        foreach (QuickstartEntry entry in entries)
        {
            if (!HasRequiredFields(entry))
            {
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

    private bool HasRequiredFields(QuickstartEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id) ||
            string.IsNullOrWhiteSpace(entry.Language) ||
            string.IsNullOrWhiteSpace(entry.Resource) ||
            string.IsNullOrWhiteSpace(entry.RepositoryUrl) ||
            string.IsNullOrWhiteSpace(entry.FolderPath))
        {
            _logger.LogDebug("Dropping quickstart entry with missing required field: '{Id}'.", entry.Id);
            return false;
        }

        return true;
    }

    private static bool HasValidGitRef(QuickstartEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.GitRef)
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
}
