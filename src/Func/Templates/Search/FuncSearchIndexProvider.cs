// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Resolves the func template search index from a local-file override, an
/// override URI, or the default index URI. The remote path caches the index in
/// the func hive, revalidates with an ETag once the cached copy ages past the
/// freshness window, and falls back to a stale copy (with a warning) when the
/// network is unavailable. A local-file override is served without any network
/// access.
/// </summary>
internal sealed class FuncSearchIndexProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<FuncTemplateSearchOptions> options,
    TimeProvider timeProvider,
    ILogger<FuncSearchIndexProvider> logger) : IFuncSearchIndexProvider
{
    private const string IndexFileName = "nugetTemplateSearchInfo.json";
    private const string MetaFileName = "nugetTemplateSearchInfo.meta.json";
    private const string HttpSchemePrefix = "http";

    private static readonly JsonSerializerOptions _metaSerializerOptions = new() { WriteIndented = false };

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly FuncTemplateSearchOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly ILogger<FuncSearchIndexProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<FuncSearchIndex> GetIndexAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (TryGetLocalFilePath(_options.IndexUri, out string? localPath))
        {
            return await LoadFromLocalFileAsync(localPath!, cancellationToken);
        }

        Directory.CreateDirectory(_options.CacheDirectory);
        string indexPath = Path.Combine(_options.CacheDirectory, IndexFileName);

        if (_options.LocalOnly)
        {
            _logger.LogDebug("Local-only search mode; skipping download of {IndexUri}", _options.IndexUri);
            return LoadCached(indexPath)
                ?? throw new InvalidOperationException(
                    $"No cached template search index exists at '{indexPath}' and local-only mode is enabled. "
                    + "Run once with network access, or point the search override at a local index file.");
        }

        FuncSearchIndexCacheMeta? meta = TryReadMeta();
        if (meta is not null && IsCacheFresh(meta, indexPath))
        {
            _logger.LogDebug("Template search index cache is fresh; using cached copy.");
            FuncSearchIndex? fresh = LoadCached(indexPath);
            if (fresh is not null)
            {
                return fresh;
            }
        }

        try
        {
            FuncSearchIndex? downloaded = await TryDownloadAsync(indexPath, meta?.ETag, cancellationToken);
            if (downloaded is not null)
            {
                return downloaded;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Network failure or HTTP timeout: fall back to a cached copy below.
            _logger.LogWarning(ex, "Failed to download template search index from {IndexUri}", _options.IndexUri);
        }

        FuncSearchIndex? stale = LoadCached(indexPath);
        if (stale is not null)
        {
            _logger.LogWarning("Using a stale cached template search index; the latest could not be downloaded.");
            return stale;
        }

        throw new InvalidOperationException(
            $"Unable to download the template search index from '{_options.IndexUri}' and no cached copy is available. "
            + "Check your network connection, or set FUNC_CLI_TEMPLATE_SEARCH_INDEX to a local index file for offline use.");
    }

    internal static bool TryGetLocalFilePath(string indexUri, out string? localPath)
    {
        localPath = null;

        if (Uri.TryCreate(indexUri, UriKind.Absolute, out Uri? uri) && uri.IsFile && !uri.IsUnc)
        {
            localPath = uri.LocalPath;
            return true;
        }

        if (!indexUri.StartsWith(HttpSchemePrefix, StringComparison.OrdinalIgnoreCase)
            && !indexUri.StartsWith("//", StringComparison.Ordinal)
            && !indexUri.StartsWith(@"\\", StringComparison.Ordinal)
            && IsAbsolutePath(indexUri))
        {
            localPath = indexUri;
            return true;
        }

        return false;
    }

    private async Task<FuncSearchIndex> LoadFromLocalFileAsync(string path, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Loading template search index from local file '{Path}'.", path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Template search index override file '{path}' does not exist. "
                + "Check the FUNC_CLI_TEMPLATE_SEARCH_INDEX environment variable.",
                path);
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return FuncSearchIndexReader.Parse(json);
    }

    private async Task<FuncSearchIndex?> TryDownloadAsync(string indexPath, string? etag, CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient(TemplateSearchRegistration.HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, _options.IndexUri);
        if (!string.IsNullOrEmpty(etag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            _logger.LogDebug("Template search index not modified (304); refreshing cache timestamp.");
            WriteMeta(new FuncSearchIndexCacheMeta(etag ?? string.Empty, _timeProvider.GetUtcNow(), _options.IndexUri));
            return LoadCached(indexPath);
        }

        response.EnsureSuccessStatusCode();

        string content = await response.Content.ReadAsStringAsync(cancellationToken);
        FuncSearchIndex index = FuncSearchIndexReader.Parse(content);

        await File.WriteAllTextAsync(indexPath, content, cancellationToken);
        WriteMeta(new FuncSearchIndexCacheMeta(response.Headers.ETag?.Tag ?? string.Empty, _timeProvider.GetUtcNow(), _options.IndexUri));
        return index;
    }

    private FuncSearchIndex? LoadCached(string indexPath)
    {
        if (!File.Exists(indexPath))
        {
            return null;
        }

        try
        {
            return FuncSearchIndexReader.Parse(File.ReadAllText(indexPath));
        }
        catch (FuncSearchIndexFormatException ex)
        {
            _logger.LogWarning(ex, "Cached template search index at {IndexPath} is corrupt.", indexPath);
            return null;
        }
    }

    private bool IsCacheFresh(FuncSearchIndexCacheMeta meta, string indexPath)
        => _timeProvider.GetUtcNow() - meta.CachedAt < _options.CacheTtl
        && string.Equals(meta.SourceUri, _options.IndexUri, StringComparison.Ordinal)
        && File.Exists(indexPath);

    private FuncSearchIndexCacheMeta? TryReadMeta()
    {
        string metaPath = Path.Combine(_options.CacheDirectory, MetaFileName);
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FuncSearchIndexCacheMeta>(File.ReadAllText(metaPath), _metaSerializerOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to read template search index cache metadata.");
            return null;
        }
    }

    private void WriteMeta(FuncSearchIndexCacheMeta meta)
    {
        string metaPath = Path.Combine(_options.CacheDirectory, MetaFileName);
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, _metaSerializerOptions));
    }

    private static bool IsAbsolutePath(string path)
    {
        if (path.StartsWith('/'))
        {
            return true;
        }

        return path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';
    }

    private sealed record FuncSearchIndexCacheMeta(
        [property: JsonPropertyName("etag")] string ETag,
        [property: JsonPropertyName("cachedAt")] DateTimeOffset CachedAt,
        [property: JsonPropertyName("sourceUri")] string SourceUri);
}
