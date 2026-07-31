// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/NuGet/NugetPackProvider.cs
//   version: 10.0.302
// The V3 search/download plumbing follows this repo's own
// src/Func/Workloads/Catalog/NuGetProtocolSourceClient.cs. See README.md for full provenance.

using System.Globalization;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Discovers candidate packages from a remote V3 NuGet feed by querying its <c>SearchQueryService</c>
/// once per configured func package type, then downloading each candidate on demand.
/// </summary>
internal sealed class NuGetFeedPackageProvider : IPackageProvider, IDisposable
{
    private const int PageSize = 100;
    private const int MaxResultsPerType = 1000;

    private readonly SourceRepository _repository;
    private readonly IReadOnlyList<string> _packageTypes;
    private readonly bool _includePrerelease;
    private readonly SourceCacheContext _cache = new();
    private string? _downloadDirectory;

    public NuGetFeedPackageProvider(string feedUrl, IReadOnlyList<string> packageTypes, bool includePrerelease)
    {
        ArgumentException.ThrowIfNullOrEmpty(feedUrl);
        _packageTypes = packageTypes ?? throw new ArgumentNullException(nameof(packageTypes));
        _includePrerelease = includePrerelease;
        _repository = Repository.Factory.GetCoreV3(feedUrl);
    }

    public string Name => "NuGetFeedProvider";

    public async IAsyncEnumerable<CandidatePackage> GetCandidatesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ServiceIndexResourceV3 serviceIndex = await _repository.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken)
            ?? throw new InvalidOperationException($"Source '{_repository.PackageSource.Source}' does not expose a V3 service index; a remote feed must be a V3 NuGet feed.");

        string? searchBaseUrl = serviceIndex.GetServiceEntryUri(
            "SearchQueryService",
            "SearchQueryService/3.5.0",
            "SearchQueryService/3.0.0-rc",
            "SearchQueryService/3.0.0-beta")?.AbsoluteUri;

        if (string.IsNullOrEmpty(searchBaseUrl))
        {
            throw new InvalidOperationException($"Source '{_repository.PackageSource.Source}' does not advertise a SearchQueryService entry.");
        }

        HttpSourceResource httpSource = await _repository.GetResourceAsync<HttpSourceResource>(cancellationToken);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string packageType in _packageTypes)
        {
            for (int skip = 0; skip < MaxResultsPerType; skip += PageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Uri searchUri = BuildSearchUri(searchBaseUrl, packageType, skip);
                JObject? response = await httpSource.HttpSource.GetJObjectAsync(
                    new HttpSourceRequest(searchUri, NullLogger.Instance),
                    NullLogger.Instance,
                    cancellationToken);

                if (response?["data"] is not JArray data || data.Count == 0)
                {
                    break;
                }

                foreach (CandidatePackage candidate in ParseHits(data, packageType, seen))
                {
                    yield return candidate;
                }

                if (data.Count < PageSize)
                {
                    break;
                }
            }
        }
    }

    public async Task<string?> EnsureLocalAsync(CandidatePackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!NuGetVersion.TryParse(package.Version, out NuGetVersion? version))
        {
            return null;
        }

        _downloadDirectory ??= Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "func-template-discovery-" + Path.GetRandomFileName())).FullName;

        string targetPath = Path.Combine(_downloadDirectory, $"{package.Name}.{package.Version}.nupkg");
        FindPackageByIdResource findResource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);

        await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        bool copied = await findResource.CopyNupkgToStreamAsync(package.Name, version, fileStream, _cache, NullLogger.Instance, cancellationToken);
        return copied ? targetPath : null;
    }

    public void CleanupDownloads()
    {
        if (_downloadDirectory is null || !Directory.Exists(_downloadDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(_downloadDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: leftover temp packages in the OS temp dir are harmless and get cleaned by the OS.
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
        CleanupDownloads();
    }

    private Uri BuildSearchUri(string baseUrl, string packageType, int skip)
    {
        var qs = new List<string>
        {
            "q=template",
            "skip=" + skip.ToString(CultureInfo.InvariantCulture),
            "take=" + PageSize.ToString(CultureInfo.InvariantCulture),
            "prerelease=" + _includePrerelease.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            "semVerLevel=2.0.0",
            "packageType=" + Uri.EscapeDataString(packageType),
        };

        return new Uri(baseUrl + (baseUrl.Contains('?') ? "&" : "?") + string.Join("&", qs));
    }

    private static IEnumerable<CandidatePackage> ParseHits(JArray data, string packageType, HashSet<string> seen)
    {
        foreach (JToken hit in data)
        {
            string? id = (string?)hit["id"];
            string? versionString = (string?)hit["version"];
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(versionString) || !NuGetVersion.TryParse(versionString, out _))
            {
                continue;
            }

            if (!HitDeclaresPackageType(hit["packageTypes"], packageType) || !seen.Add(id))
            {
                continue;
            }

            yield return new CandidatePackage(
                Name: id,
                Version: versionString,
                TotalDownloads: (long?)hit["totalDownloads"] ?? 0,
                Owners: ReadOwners(hit["owners"]),
                Reserved: (bool?)hit["verified"] ?? false,
                Description: (string?)hit["description"],
                IconUrl: (string?)hit["iconUrl"],
                LocalPath: null);
        }
    }

    private static IReadOnlyList<string> ReadOwners(JToken? owners)
    {
        if (owners is JArray array)
        {
            return array.Select(o => (string?)o).Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o!).ToList();
        }

        return owners is JValue { Value: string s } && !string.IsNullOrWhiteSpace(s) ? [s] : [];
    }

    private static bool HitDeclaresPackageType(JToken? packageTypes, string expected)
    {
        // nuget.org honours packageType= server-side, but not every feed does; re-filter defensively.
        if (packageTypes is not JArray array || array.Count == 0)
        {
            return true;
        }

        foreach (JToken entry in array)
        {
            string? name = (string?)entry["name"] ?? (entry as JValue)?.Value as string;
            if (!string.IsNullOrEmpty(name) && string.Equals(name, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
