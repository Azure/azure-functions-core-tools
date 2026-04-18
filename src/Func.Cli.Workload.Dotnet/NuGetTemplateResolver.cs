// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Resolves template pack versions from NuGet, falling back to a bundled
/// version when offline or when NuGet is unreachable.
/// </summary>
internal class NuGetTemplateResolver
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    private const string NuGetV3FlatUrl = "https://api.nuget.org/v3-flatcontainer";

    /// <summary>
    /// Resolves the version to install for a given template package.
    /// Tries to fetch the latest stable version from NuGet; falls back to
    /// <paramref name="fallbackVersion"/> if the query fails.
    /// </summary>
    public static async Task<string> ResolveVersionAsync(
        string packageId,
        string fallbackVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latestVersion = await GetLatestStableVersionAsync(packageId, cancellationToken);
            return latestVersion ?? fallbackVersion;
        }
        catch
        {
            // Offline, timeout, DNS failure, etc. — use bundled version
            return fallbackVersion;
        }
    }

    /// <summary>
    /// Queries the NuGet v3 flat container API for the latest stable version.
    /// Returns null if no stable versions are found.
    /// </summary>
    private static async Task<string?> GetLatestStableVersionAsync(
        string packageId,
        CancellationToken cancellationToken)
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

        // Versions are listed oldest → newest. Walk backwards to find
        // the latest stable version (no prerelease suffix).
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
}
