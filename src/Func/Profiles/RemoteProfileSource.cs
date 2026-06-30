// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Security.Cryptography;
using Azure.Functions.Cli.Configuration;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Profiles;

/// <summary>
/// Fetches the built-in profile registry from the CDN, falling back to a local cache.
/// </summary>
internal sealed class RemoteProfileSource : IProfileSource
{
    internal const string HttpClientName = "ProfileRegistry";

    private const string ProdBaseUrl = "https://cdn.functions.azure.com";
    private const string StagingBaseUrl = "https://cdn-staging.functions.azure.com";
    private const string RegistryPath = "/public/cli/v5/profiles/v1/registry.json";
    private const string ChecksumPath = "/public/cli/v5/profiles/v1/registry.json.sha256";

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

    internal static string RegistryUrl => GetBaseUrl() + RegistryPath;

    internal static string ChecksumUrl => GetBaseUrl() + ChecksumPath;

    public async Task<ProfileSourceSnapshot> LoadAsync(ProfileSourceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string cacheDir = Path.Combine(_configurationPaths.Home, CacheDirectoryName);
        string cachePath = Path.Combine(cacheDir, CacheFileName);
        string cacheChecksumPath = Path.Combine(cacheDir, CacheChecksumFileName);
        string cacheMetaPath = Path.Combine(cacheDir, CacheMetaFileName);

        // Try cached registry first (within TTL, checksum-validated)
        string? cachedJson = await TryLoadValidatedCacheAsync(cachePath, cacheChecksumPath, cacheMetaPath, cancellationToken);
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

        // Fall back to stale cache (beyond TTL but still checksum-valid)
        string? staleCachedJson = await TryLoadValidatedCacheContentAsync(cachePath, cacheChecksumPath, cancellationToken);
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

    private async Task<string?> TryLoadValidatedCacheAsync(string cachePath, string checksumPath, string metaPath, CancellationToken cancellationToken)
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

        return await TryLoadValidatedCacheContentAsync(cachePath, checksumPath, cancellationToken);
    }

    private async Task<string?> TryLoadValidatedCacheContentAsync(string cachePath, string checksumPath, CancellationToken cancellationToken)
    {
        string? json = await _fileSystem.ReadAllTextIfExistsAsync(cachePath, cancellationToken);
        if (json is null)
        {
            return null;
        }

        string? expectedChecksum = await _fileSystem.ReadAllTextIfExistsAsync(checksumPath, cancellationToken);
        if (expectedChecksum is null)
        {
            // No checksum file — treat cache as unverifiable
            _logger.LogDebug("Cached profile registry has no checksum file; discarding.");
            return null;
        }

        string actualChecksum = ComputeSha256(json);
        if (!string.Equals(actualChecksum, expectedChecksum.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Cached profile registry checksum mismatch; discarding corrupt cache.");
            return null;
        }

        return json;
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

            using HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

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

            // Persist to cache atomically: write to temp files then rename so
            // concurrent CLI processes never see a half-written cache.
            await _fileSystem.EnsureDirectoryExistsAsync(cacheDir, cancellationToken);
            await _fileSystem.WriteAllTextAtomicAsync(cachePath, registryJson, cancellationToken);
            await _fileSystem.WriteAllTextAtomicAsync(cacheChecksumPath, expectedChecksum, cancellationToken);
            // Meta written last: a partial write looks like an expired cache (safe).
            await _fileSystem.WriteAllTextAtomicAsync(cacheMetaPath, DateTimeOffset.UtcNow.ToString("O"), cancellationToken);

            _logger.LogDebug("Profile registry fetched and cached from CDN.");
            return registryJson;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User-initiated cancellation (Ctrl+C) — propagate immediately
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Timeout or network failure — fall back gracefully
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

    private static string GetBaseUrl()
    {
        string? version = typeof(RemoteProfileSource).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        bool isPrerelease = version?.Contains('-') == true;
        return isPrerelease ? StagingBaseUrl : ProdBaseUrl;
    }

    private static string ComputeSha256(string content)
    {
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }
}
