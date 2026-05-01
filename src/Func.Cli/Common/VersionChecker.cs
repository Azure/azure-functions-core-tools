// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Checks GitHub Releases for newer versions of Core Tools v5.
/// Results are cached locally for 24 hours to avoid repeated API calls.
/// All operations are best-effort — offline/error scenarios silently return null.
/// </summary>
internal static class VersionChecker
{
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan _httpTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Checks if a newer v5 release is available. Returns the latest version string
    /// if an update is available, or null if current/offline/error.
    /// </summary>
    public static async Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            if (currentVersion is null)
            {
                return null;
            }

            // Check cache first
            var cached = ReadCache();
            if (cached is not null)
            {
                return IsNewer(cached, currentVersion) ? cached.ToString() : null;
            }

            // Query GitHub
            var latestVersion = await FetchLatestV5VersionAsync(cancellationToken);
            if (latestVersion is null)
            {
                return null;
            }

            // Cache the result
            WriteCache(latestVersion);

            return IsNewer(latestVersion, currentVersion) ? latestVersion.ToString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<Version?> FetchLatestV5VersionAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { Timeout = _httpTimeout };
        httpClient.DefaultRequestHeaders.Add("User-Agent", "AzureFunctionsCoreToolsClient");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

        // Fetch first page of releases (30 results — more than enough to find latest v5)
        var releases = await httpClient.GetFromJsonAsync<GitHubRelease[]>(
            Constants.GitHubReleasesApiUrl + "?per_page=30",
            cancellationToken);

        if (releases is null)
        {
            return null;
        }

        Version? latest = null;
        foreach (var release in releases)
        {
            if (release.Draft || release.Prerelease || string.IsNullOrEmpty(release.TagName))
            {
                continue;
            }

            var tag = release.TagName.TrimStart('v');
            if (!tag.StartsWith("5.", StringComparison.Ordinal))
            {
                continue;
            }

            if (Version.TryParse(tag, out var version) && (latest is null || version > latest))
            {
                latest = version;
            }
        }

        return latest;
    }

    private static Version? GetCurrentVersion()
    {
        var versionString = typeof(VersionChecker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrEmpty(versionString))
        {
            return null;
        }

        // Strip prerelease suffix (e.g., "5.0.0-preview.1+abc" → "5.0.0")
        var dashIndex = versionString.IndexOf('-');
        if (dashIndex > 0)
        {
            versionString = versionString[..dashIndex];
        }

        var plusIndex = versionString.IndexOf('+');
        if (plusIndex > 0)
        {
            versionString = versionString[..plusIndex];
        }

        return Version.TryParse(versionString, out var version) ? version : null;
    }

    private static bool IsNewer(Version latest, Version current)
        => latest > current;

    private static string GetCachePath()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Constants.FuncHomeDirectoryName);
        return Path.Combine(cacheDir, Constants.VersionCacheFileName);
    }

    private static Version? ReadCache()
    {
        try
        {
            var cachePath = GetCachePath();
            if (!File.Exists(cachePath))
            {
                return null;
            }

            var info = new FileInfo(cachePath);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > _cacheDuration)
            {
                return null; // Cache expired
            }

            var content = File.ReadAllText(cachePath).Trim();
            return Version.TryParse(content, out var version) ? version : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCache(Version version)
    {
        try
        {
            var cachePath = GetCachePath();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllText(cachePath, version.ToString());
        }
        catch
        {
            // Best-effort
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
    }
}
