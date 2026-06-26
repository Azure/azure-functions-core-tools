// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using Azure.Functions.Cli.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Fetches the built-in profile registry from the CDN, falling back to a local cache.
/// </summary>
internal sealed class RemoteProfileSource : IProfileSource
{
    internal const string RegistryUrl = "https://cdn-staging.functions.azure.com/public/cli/v5/profiles/v1/registry.json";
    internal const string ChecksumUrl = "https://cdn-staging.functions.azure.com/public/cli/v5/profiles/v1/registry.json.sha256";
    internal const string HttpClientName = "ProfileRegistry";

    private const string CacheFileName = "registry.json";
    private const string CacheChecksumFileName = "registry.json.sha256";
    private const string CacheMetaFileName = "registry.json.meta";
    private const string CacheDirectoryName = "profiles";

    private static readonly TimeSpan _cacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan _stalenessWarningThreshold = TimeSpan.FromDays(7);
    private static readonly TimeSpan _fetchTimeout = TimeSpan.FromSeconds(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProfileDocumentParser _parser;
    private readonly IProfileFileSystem _fileSystem;
    private readonly CliConfigurationPathsOptions _configurationPaths;
    private readonly ILogger<RemoteProfileSource> _logger;

    public RemoteProfileSource(
        IHttpClientFactory httpClientFactory,
        ProfileDocumentParser parser,
        IProfileFileSystem fileSystem,
        CliConfigurationPathsOptions configurationPaths,
        ILogger<RemoteProfileSource> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _configurationPaths = configurationPaths ?? throw new ArgumentNullException(nameof(configurationPaths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string cacheDir = Path.Combine(_configurationPaths.Home, CacheDirectoryName);
        string cachePath = Path.Combine(cacheDir, CacheFileName);
        string cacheChecksumPath = Path.Combine(cacheDir, CacheChecksumFileName);
        string cacheMetaPath = Path.Combine(cacheDir, CacheMetaFileName);

        // Try cached registry first (within TTL)
        string? cachedJson = await TryLoadCacheAsync(cachePath, cacheMetaPath, cancellationToken);
        if (cachedJson is not null)
        {
            _logger.LogDebug("Using cached profile registry (within TTL).");
            var cacheSource = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "cached registry", cachePath);
            return _parser.ParseBuiltInRegistry(cachedJson, cacheSource);
        }

        // Try remote fetch
        string? remoteJson = await TryFetchRemoteAsync(cacheDir, cachePath, cacheChecksumPath, cacheMetaPath, cancellationToken);
        if (remoteJson is not null)
        {
            var remoteSource = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "remote registry", cachePath);
            return _parser.ParseBuiltInRegistry(remoteJson, remoteSource);
        }

        // Fall back to stale cache (beyond TTL but still usable)
        string? staleCachedJson = await _fileSystem.ReadAllTextIfExistsAsync(cachePath, cancellationToken);
        if (staleCachedJson is not null)
        {
            DateTimeOffset? fetchTime = await ReadFetchTimestampAsync(cacheMetaPath, cancellationToken);
            if (fetchTime is not null)
            {
                TimeSpan age = DateTimeOffset.UtcNow - fetchTime.Value;
                if (age > _stalenessWarningThreshold)
                {
                    _logger.LogWarning("Profiles are {Days} days old. Host versions may not match current cloud deployments.", (int)age.TotalDays);
                }
            }

            _logger.LogDebug("Using stale cached profile registry (remote fetch failed).");
            var staleCacheSource = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "cached registry (stale)", cachePath);
            return _parser.ParseBuiltInRegistry(staleCachedJson, staleCacheSource);
        }

        // No remote, no cache — return empty so BuiltInProfileSource (bundled fallback) handles it
        _logger.LogDebug("No remote or cached profile registry available; deferring to bundled fallback.");
        var emptySource = new ProfileSourceInfo(ProfileSourceKind.BuiltIn, "remote registry (unavailable)");
        return ProfileSourceSnapshot.Empty(emptySource);
    }

    private async Task<string?> TryLoadCacheAsync(string cachePath, string metaPath, CancellationToken cancellationToken)
    {
        DateTimeOffset? fetchTime = await ReadFetchTimestampAsync(metaPath, cancellationToken);
        if (fetchTime is null)
        {
            return null;
        }

        if (DateTimeOffset.UtcNow - fetchTime.Value > _cacheTtl)
        {
            return null;
        }

        return await _fileSystem.ReadAllTextIfExistsAsync(cachePath, cancellationToken);
    }

    private async Task<string?> TryFetchRemoteAsync(
        string cacheDir,
        string cachePath,
        string cacheChecksumPath,
        string cacheMetaPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_fetchTimeout);

            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

            // Fetch registry and checksum in parallel
            Task<string> registryTask = client.GetStringAsync(RegistryUrl, cts.Token);
            Task<string> checksumTask = client.GetStringAsync(ChecksumUrl, cts.Token);

            await Task.WhenAll(registryTask, checksumTask);

            string registryJson = await registryTask;
            string expectedChecksum = (await checksumTask).Trim();

            // Validate checksum
            string actualChecksum = ComputeSha256(registryJson);
            if (!string.Equals(actualChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Profile registry checksum mismatch. Discarding remote fetch.");
                return null;
            }

            // Persist to cache
            Directory.CreateDirectory(cacheDir);
            await _fileSystem.WriteAllTextAsync(cachePath, registryJson, cancellationToken);
            await _fileSystem.WriteAllTextAsync(cacheChecksumPath, expectedChecksum, cancellationToken);
            await _fileSystem.WriteAllTextAsync(cacheMetaPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

            _logger.LogDebug("Profile registry fetched and cached from CDN.");
            return registryJson;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to fetch profile registry from CDN.");
            return null;
        }
    }

    private async Task<DateTimeOffset?> ReadFetchTimestampAsync(string metaPath, CancellationToken cancellationToken)
    {
        string? meta = await _fileSystem.ReadAllTextIfExistsAsync(metaPath, cancellationToken);
        if (meta is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(meta.Trim(), out DateTimeOffset ts) ? ts : null;
    }

    private static string ComputeSha256(string content)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }
}
