// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Checks NuGet for newer versions of installed workloads and notifies
/// the user. Results are cached for 24 hours to avoid repeated API calls.
/// </summary>
internal class WorkloadUpdateChecker
{
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromHours(24);

    private static readonly string _defaultCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".azure-functions",
        "workload-update-cache.json");

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private const string NuGetV3FlatUrl = "https://api.nuget.org/v3-flatcontainer";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _cachePath;

    public WorkloadUpdateChecker(string? cachePath = null)
    {
        _cachePath = cachePath ?? _defaultCachePath;
    }

    /// <summary>
    /// Checks all installed workloads for available updates.
    /// Returns a list of workloads that have a newer version on NuGet.
    /// Skips workloads that were checked within the last 24 hours.
    /// </summary>
    public async Task<IReadOnlyList<WorkloadUpdateInfo>> CheckForUpdatesAsync(
        IReadOnlyList<WorkloadInfo> installedWorkloads,
        CancellationToken cancellationToken = default)
    {
        if (installedWorkloads.Count == 0)
        {
            return [];
        }

        var cache = ReadCache();
        var updates = new List<WorkloadUpdateInfo>();
        var cacheModified = false;

        foreach (var workload in installedWorkloads)
        {
            try
            {
                var key = workload.PackageId.ToLowerInvariant();
                cache.Entries.TryGetValue(key, out var entry);

                // Skip if we checked recently and the installed version hasn't changed
                if (entry is not null && !IsExpired(entry)
                    && entry.InstalledVersion == workload.Version)
                {
                    if (entry.LatestVersion is not null
                        && !entry.LatestVersion.Equals(workload.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        updates.Add(new WorkloadUpdateInfo(workload.Id, workload.Version, entry.LatestVersion));
                    }
                    continue;
                }

                var latestVersion = await GetLatestStableVersionAsync(
                    workload.PackageId, cancellationToken);

                // Update cache
                entry = new UpdateCacheEntry
                {
                    InstalledVersion = workload.Version,
                    LatestVersion = latestVersion,
                    LastCheckedUtc = DateTimeOffset.UtcNow
                };
                cache.Entries[key] = entry;
                cacheModified = true;

                if (latestVersion is not null
                    && !latestVersion.Equals(workload.Version, StringComparison.OrdinalIgnoreCase))
                {
                    updates.Add(new WorkloadUpdateInfo(workload.Id, workload.Version, latestVersion));
                }
            }
            catch
            {
                // Don't let update checks break the command
            }
        }

        if (cacheModified)
        {
            WriteCache(cache);
        }

        return updates;
    }

    private static async Task<string?> GetLatestStableVersionAsync(
        string packageId, CancellationToken cancellationToken)
    {
        var url = $"{NuGetV3FlatUrl}/{packageId.ToLowerInvariant()}/index.json";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("versions", out var versions))
        {
            return null;
        }

        // Versions are listed oldest → newest. Walk to find the latest stable.
        string? latest = null;
        foreach (var v in versions.EnumerateArray())
        {
            var version = v.GetString();
            if (version is not null && !version.Contains('-'))
            {
                latest = version;
            }
        }

        return latest;
    }

    private UpdateCache ReadCache()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath);
                return JsonSerializer.Deserialize<UpdateCache>(json, _jsonOptions) ?? new UpdateCache();
            }
        }
        catch
        {
            // Corrupt cache — start fresh
        }

        return new UpdateCache();
    }

    private void WriteCache(UpdateCache cache)
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (dir is not null)
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(cache, _jsonOptions);
            File.WriteAllText(_cachePath, json);
        }
        catch
        {
            // Best-effort — don't fail the command
        }
    }

    private static bool IsExpired(UpdateCacheEntry entry)
    {
        return DateTimeOffset.UtcNow - entry.LastCheckedUtc > _cacheTtl;
    }

    internal class UpdateCache
    {
        [JsonPropertyName("entries")]
        public Dictionary<string, UpdateCacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal class UpdateCacheEntry
    {
        [JsonPropertyName("installedVersion")]
        public string? InstalledVersion { get; set; }

        [JsonPropertyName("latestVersion")]
        public string? LatestVersion { get; set; }

        [JsonPropertyName("lastCheckedUtc")]
        public DateTimeOffset LastCheckedUtc { get; set; }
    }
}

/// <summary>
/// Info about an available workload update.
/// </summary>
internal record WorkloadUpdateInfo(string WorkloadId, string InstalledVersion, string LatestVersion);
