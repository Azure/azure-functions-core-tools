// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Fetches the quickstart manifest from the CDN with ETag-based caching and
/// multi-tier fallback (primary CDN → backup URL → cached copy on disk).
/// </summary>
internal class QuickstartManifestClient : IQuickstartManifestClient
{
    // Languages that indicate infrastructure-as-code templates, which are excluded.
    private static readonly HashSet<string> _iacLanguages =
        new(StringComparer.OrdinalIgnoreCase) { "ARM", "Bicep", "Terraform" };

    private const string ManifestFileName = "manifest.json";
    private const string ManifestMetaFileName = "manifest-meta.json";

    private readonly HttpClient _httpClient;
    private readonly QuickstartManifestOptions _options;
    private readonly ILogger<QuickstartManifestClient> _logger;

    public QuickstartManifestClient(
        HttpClient httpClient,
        IOptions<QuickstartManifestOptions> options,
        ILogger<QuickstartManifestClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<QuickstartManifest> GetManifestAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_options.CacheDirectory);

        // Local file overrides bypass HTTP and caching entirely — the file is the
        // source of truth and reading it is already fast.
        if (TryGetLocalFilePath(_options.ManifestUrl, out string? localPath))
        {
            return await LoadFromLocalFileAsync(localPath, cancellationToken);
        }

        ManifestMeta? cachedMeta = TryReadMeta();
        if (cachedMeta is not null && IsCacheFresh(cachedMeta))
        {
            _logger.LogDebug("Quickstart manifest cache is fresh; using cached copy.");
            return LoadCachedManifest();
        }

        // Try primary CDN with ETag conditional request.
        try
        {
            QuickstartManifest? result = await TryFetchFromUrlAsync(
                _options.ManifestUrl, cachedMeta?.ETag, cancellationToken);

            if (result is not null)
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Primary CDN fetch failed; trying backup URL.");
        }

        // Try backup URL.
        try
        {
            QuickstartManifest? result = await TryFetchFromUrlAsync(
                _options.BackupManifestUrl, etag: null, cancellationToken);

            if (result is not null)
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Backup URL fetch failed; using cached copy.");
        }

        // Last resort: return whatever is cached.
        QuickstartManifest? cached = TryLoadCachedManifest();
        if (cached is not null)
        {
            return cached;
        }

        throw new InvalidOperationException(
            "Unable to fetch the quickstart manifest and no cached copy is available.");
    }

    private async Task<QuickstartManifest?> TryFetchFromUrlAsync(
        string url, string? etag, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using HttpResponseMessage response = await SendManifestRequestAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogDebug("Manifest not modified (304); refreshing cache timestamp.");
            UpdateCacheTimestamp(etag!);
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

        WriteCacheFiles(content, newEtag ?? string.Empty);

        return BuildManifest(entries);
    }

    /// <summary>
    /// Virtual seam for tests to intercept outgoing HTTP requests.
    /// </summary>
    protected virtual Task<HttpResponseMessage> SendManifestRequestAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        _httpClient.SendAsync(request, cancellationToken);

    private static bool TryGetLocalFilePath(string url, out string localPath)
    {
        localPath = string.Empty;

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.IsFile)
        {
            localPath = uri.LocalPath;
            return true;
        }

        return false;
    }

    private async Task<QuickstartManifest> LoadFromLocalFileAsync(
        string path, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading quickstart manifest from local file '{Path}'.", path);

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Manifest override file '{path}' does not exist. " +
                $"Check the {QuickstartRegistration.ManifestUrlEnvVar} environment variable.");
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        List<QuickstartEntry>? entries = DeserializeEntries(json)
            ?? throw new InvalidOperationException(
                $"Manifest override file '{path}' is empty or malformed.");

        return BuildManifest(entries);
    }

    private QuickstartManifest BuildManifest(List<QuickstartEntry> entries)
    {
        var filtered = entries
            .Where(IsValidEntry)
            .Where(IsAllowedRepositoryUrl)
            .Where(e => !_iacLanguages.Contains(e.Language))
            .ToList();

        return new QuickstartManifest(filtered);
    }

    /// <summary>
    /// Drops entries missing core fields. Logs a warning for entries missing
    /// <see cref="QuickstartEntry.GitRef"/> but keeps them — once every entry in
    /// the production manifest carries a <c>gitRef</c>, this should be promoted
    /// to a hard requirement and the entry dropped instead of warned.
    /// </summary>
    private bool IsValidEntry(QuickstartEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Id) ||
            string.IsNullOrWhiteSpace(entry.Language) ||
            string.IsNullOrWhiteSpace(entry.Resource) ||
            string.IsNullOrWhiteSpace(entry.RepositoryUrl) ||
            string.IsNullOrWhiteSpace(entry.FolderPath))
        {
            _logger.LogWarning(
                "Skipping quickstart entry with missing required field: id='{Id}', " +
                "language='{Language}', resource='{Resource}', repositoryUrl='{RepositoryUrl}', " +
                "folderPath='{FolderPath}'.",
                entry.Id, entry.Language, entry.Resource, entry.RepositoryUrl, entry.FolderPath);
            return false;
        }

        if (string.IsNullOrWhiteSpace(entry.GitRef))
        {
            // TODO: tighten to hard-fail once the CDN manifest pins every entry to a gitRef.
            _logger.LogWarning(
                "Quickstart entry '{Id}' is missing 'gitRef'; falling back to HEAD. " +
                "Pin every manifest entry to a tag or commit SHA for reproducible scaffolds.",
                entry.Id);
        }

        return true;
    }

    private bool IsAllowedRepositoryUrl(QuickstartEntry entry)
    {
        if (QuickstartUrlValidator.IsAllowed(entry.RepositoryUrl))
        {
            return true;
        }

        _logger.LogWarning(
            "Skipping quickstart entry '{Id}': repository URL '{Url}' is not an allowed " +
            "HTTPS GitHub URL from a trusted organization.",
            entry.Id, entry.RepositoryUrl);
        return false;
    }

    // ── Deserialization ──────────────────────────────────────────────────

    /// <summary>
    /// Deserializes from either the CDN envelope (<c>{ "templates": [...] }</c>)
    /// or a bare array of entries (legacy cache format).
    /// </summary>
    private static List<QuickstartEntry>? DeserializeEntries(string json)
    {
        // Try envelope format first (CDN response).
        QuickstartManifestEnvelope? envelope = JsonSerializer.Deserialize(
            json, QuickstartManifestJsonContext.Default.QuickstartManifestEnvelope);

        if (envelope?.Templates is { Count: > 0 })
        {
            return envelope.Templates;
        }

        // Fall back to bare array (legacy cache files).
        return JsonSerializer.Deserialize(
            json, QuickstartManifestJsonContext.Default.ListQuickstartEntry);
    }

    // ── Cache helpers ──────────────────────────────────────────────────────

    private string ManifestFilePath =>
        Path.Combine(_options.CacheDirectory, ManifestFileName);

    private string ManifestMetaFilePath =>
        Path.Combine(_options.CacheDirectory, ManifestMetaFileName);

    private record ManifestMeta(string ETag, DateTimeOffset CachedAt);

    private ManifestMeta? TryReadMeta()
    {
        try
        {
            if (!File.Exists(ManifestMetaFilePath))
            {
                return null;
            }

            string json = File.ReadAllText(ManifestMetaFilePath);
            return JsonSerializer.Deserialize<ManifestMeta>(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    private bool IsCacheFresh(ManifestMeta meta) =>
        DateTimeOffset.UtcNow - meta.CachedAt < _options.CacheTtl &&
        File.Exists(ManifestFilePath);

    private QuickstartManifest LoadCachedManifest() =>
        TryLoadCachedManifest()
        ?? throw new InvalidOperationException("Cached manifest file is missing or corrupt.");

    private QuickstartManifest? TryLoadCachedManifest()
    {
        try
        {
            if (!File.Exists(ManifestFilePath))
            {
                return null;
            }

            string json = File.ReadAllText(ManifestFilePath);
            List<QuickstartEntry>? entries = DeserializeEntries(json);

            return entries is null ? null : BuildManifest(entries);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Failed to read cached manifest.");
            return null;
        }
    }

    private void WriteCacheFiles(string manifestJson, string etag)
    {
        File.WriteAllText(ManifestFilePath, manifestJson);
        ManifestMeta meta = new(etag, DateTimeOffset.UtcNow);
        File.WriteAllText(ManifestMetaFilePath, JsonSerializer.Serialize(meta));
    }

    private void UpdateCacheTimestamp(string etag)
    {
        ManifestMeta meta = new(etag, DateTimeOffset.UtcNow);
        File.WriteAllText(ManifestMetaFilePath, JsonSerializer.Serialize(meta));
    }
}
