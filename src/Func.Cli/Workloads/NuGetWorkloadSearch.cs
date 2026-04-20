// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Discovers workloads on NuGet by searching for packages that match the
/// <c>Azure.Functions.Cli.Workload.</c> naming convention. This enables
/// third-party workloads to be discoverable via <c>func workload search</c>
/// without requiring a CLI update.
/// </summary>
internal static class NuGetWorkloadSearch
{
    private const string SearchUrl =
        "https://azuresearch-usnc.nuget.org/query";

    private const string PackagePrefix = "Azure.Functions.Cli.Workload.";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Searches NuGet for packages matching the workload naming convention.
    /// Returns a list of <see cref="AvailableWorkload"/> entries.
    /// </summary>
    public static async Task<IReadOnlyList<AvailableWorkload>> SearchAsync(
        CancellationToken cancellationToken = default)
    {
        return await SearchAsync(null, cancellationToken);
    }

    /// <summary>
    /// Searches NuGet for packages matching the workload naming convention,
    /// optionally filtered by a query string.
    /// </summary>
    public static async Task<IReadOnlyList<AvailableWorkload>> SearchAsync(
        string? query, CancellationToken cancellationToken = default)
    {
        var searchTerm = string.IsNullOrWhiteSpace(query)
            ? PackagePrefix
            : $"{PackagePrefix} {query.Trim()}";

        var url = $"{SearchUrl}?q={Uri.EscapeDataString(searchTerm)}&take=50&prerelease=false";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<NuGetSearchResponse>(json);

        if (result?.Data is null)
        {
            return [];
        }

        var workloads = new List<AvailableWorkload>();
        foreach (var pkg in result.Data)
        {
            if (string.IsNullOrEmpty(pkg.Id) ||
                !pkg.Id.StartsWith(PackagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var shortId = pkg.Id[PackagePrefix.Length..].ToLowerInvariant();
            workloads.Add(new AvailableWorkload(
                Id: shortId,
                PackageId: pkg.Id,
                Description: pkg.Description ?? shortId,
                Languages: "",
                InstalledVersion: null));
        }

        return workloads;
    }

    // Minimal JSON models for NuGet v3 search response
    private sealed class NuGetSearchResponse
    {
        public List<NuGetPackageResult>? Data { get; set; }
    }

    private sealed class NuGetPackageResult
    {
        public string? Id { get; set; }
        public string? Description { get; set; }
        public string? Version { get; set; }
    }
}
