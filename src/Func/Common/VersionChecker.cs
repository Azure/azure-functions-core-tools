// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Azure.Functions.Cli.Configuration;

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
    public static Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        CliConfigurationPathsOptions userConfigurationPaths = new();
        return CheckForUpdateAsync(userConfigurationPaths, cancellationToken);
    }

    internal static async Task<string?> CheckForUpdateAsync(CliConfigurationPathsOptions userConfigurationPaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userConfigurationPaths);

        try
        {
            Version? currentVersion = GetCurrentVersion();
            if (currentVersion is null)
            {
                return null;
            }

            // Check cache first
            Version? cached = ReadCache(userConfigurationPaths);
            if (cached is not null)
            {
                return IsNewer(cached, currentVersion) ? cached.ToString() : null;
            }

            // Query GitHub
            Version? latestVersion = await FetchLatestV5VersionAsync(cancellationToken);
            if (latestVersion is null)
            {
                return null;
            }

            // Cache the result
            WriteCache(userConfigurationPaths, latestVersion);

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
        httpClient.DefaultRequestHeaders.Add("User-Agent", "AzureFunctionsCliClient");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

        // Fetch first page of releases (30 results — more than enough to find latest v5)
        GitHubRelease[]? releases = await httpClient.GetFromJsonAsync<GitHubRelease[]>(
            Constants.GitHubReleasesApiUrl + "?per_page=30",
            cancellationToken);

        if (releases is null)
        {
            return null;
        }

        Version? latest = null;
        foreach (GitHubRelease release in releases)
        {
            if (release.Draft || release.Prerelease || string.IsNullOrEmpty(release.TagName))
            {
                continue;
            }

            string tag = release.TagName.TrimStart('v');
            if (!tag.StartsWith("5.", StringComparison.Ordinal))
            {
                continue;
            }

            if (Version.TryParse(tag, out Version? version) && (latest is null || version > latest))
            {
                latest = version;
            }
        }

        return latest;
    }

    private static Version? GetCurrentVersion()
    {
        string? versionString = typeof(VersionChecker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (string.IsNullOrEmpty(versionString))
        {
            return null;
        }

        // Strip prerelease suffix (e.g., "5.0.0-preview.1+abc" → "5.0.0")
        int dashIndex = versionString.IndexOf('-');
        if (dashIndex > 0)
        {
            versionString = versionString[..dashIndex];
        }

        int plusIndex = versionString.IndexOf('+');
        if (plusIndex > 0)
        {
            versionString = versionString[..plusIndex];
        }

        return Version.TryParse(versionString, out Version? version) ? version : null;
    }

    private static bool IsNewer(Version latest, Version current)
        => latest > current;

    private static Version? ReadCache(CliConfigurationPathsOptions userConfigurationPaths)
    {
        try
        {
            string cachePath = userConfigurationPaths.VersionCachePath;
            if (!File.Exists(cachePath))
            {
                return null;
            }

            var info = new FileInfo(cachePath);
            if (DateTime.UtcNow - info.LastWriteTimeUtc > _cacheDuration)
            {
                return null; // Cache expired
            }

            string content = File.ReadAllText(cachePath).Trim();
            return Version.TryParse(content, out Version? version) ? version : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteCache(CliConfigurationPathsOptions userConfigurationPaths, Version version)
    {
        try
        {
            string cachePath = userConfigurationPaths.VersionCachePath;
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
