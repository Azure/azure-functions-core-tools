// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Azure.Functions.Cli.Workloads.Install;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// V3 NuGet feed client built on <c>NuGet.Protocol</c>. Drives search via
/// the source's <c>SearchQueryService</c> entry (with the <c>packageType=</c>
/// filter) and version/download via <see cref="FindPackageByIdResource"/>,
/// restricting search to the <c>FuncCliWorkload</c> package type.
/// </summary>
internal class NuGetProtocolSourceClient(SourceRepository repository)
{
    private const string FuncCliWorkloadPackageType = "FuncCliWorkload";

    // SearchFilter.PackageTypes is serialised by NuGet.Client as
    // 'packageTypeFilter=', which nuget.org silently ignores (verified via
    // probe-nuget-package-type-filter.ps1). The wiki-spec parameter
    // 'packageType=' (singular, query-string) is the one nuget.org honours,
    // and no typed NuGet.Client API exposes it. So we hand-build the search
    // request against the V3 service index and trust the server-side filter.
    // Wiki: https://github.com/NuGet/Home/wiki/Search-by-Package-Type-and-Query-Language-Surfacing
    private const string PackageTypeQueryParam = "packageType";

    private readonly SourceRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public PackageSource Source => _repository.PackageSource;

    public async Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(CatalogSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int take = query.Take ?? CatalogSearchQuery.DefaultTake;
        Uri? searchUri = await TryBuildV3SearchUriAsync(query, take, cancellationToken);

        if (searchUri is null)
        {
            throw new InvalidOperationException(
                $"Source '{Source.Source}' does not advertise a SearchQueryService entry. Workload search requires a V3 NuGet feed.");
        }

        JObject? raw = await FetchSearchResponseAsync(searchUri, cancellationToken);
        return raw is null ? [] : ParseV3Hits(raw, Source);
    }

    public async Task<IReadOnlyList<NuGetVersion>> ListVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        FindPackageByIdResource findResource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        using var cache = new SourceCacheContext();

        IEnumerable<NuGetVersion> versions = await findResource.GetAllVersionsAsync(
            packageId,
            cache,
            NullLogger.Instance,
            cancellationToken);

        return versions?.ToList() ?? [];
    }

    public async Task<Stream> OpenPackageAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        FindPackageByIdResource findResource = await _repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        using var cache = new SourceCacheContext();

        // Spool to a temp file so the caller gets a seekable stream that
        // PackageArchiveReader can consume without keeping NuGet.Protocol's
        // internal buffers alive. The stream is returned to the caller
        // (transferring ownership), so we can't use `await using` here;
        // the try/catch guards against leaking the file if the download
        // fails before we hand it back.
        string tempPath = Path.Combine(Path.GetTempPath(), $"funccli-workload-{Path.GetRandomFileName()}.nupkg");
        FileStream fileStream = new(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096, FileOptions.DeleteOnClose);
        try
        {
            bool copied = await findResource.CopyNupkgToStreamAsync(
                packageId,
                version,
                fileStream,
                cache,
                NullLogger.Instance,
                cancellationToken);

            if (!copied)
            {
                throw new WorkloadPackageNotFoundException(
                    $"Package '{packageId}' {version} was not found on source '{Source.Name}'.");
            }

            fileStream.Position = 0;
            return fileStream;
        }
        catch
        {
            await fileStream.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Issues the V3 search request through NuGet's <see cref="HttpSource"/>
    /// so credential providers, retries, proxy, and HTTP cache configured
    /// for the source all apply. Virtual so tests can stub the JSON
    /// response without standing up an HTTP server.
    /// </summary>
    internal virtual async Task<JObject?> FetchSearchResponseAsync(Uri searchUri, CancellationToken cancellationToken)
    {
        HttpSourceResource httpSourceResource = await _repository.GetResourceAsync<HttpSourceResource>(cancellationToken);
        return await httpSourceResource.HttpSource.GetJObjectAsync(
            new HttpSourceRequest(searchUri, NullLogger.Instance),
            NullLogger.Instance,
            cancellationToken);
    }

    /// <summary>
    /// Resolves the source's <c>SearchQueryService</c> entry from its V3
    /// service index and builds a request URL with the
    /// <c>packageType=FuncCliWorkload</c> filter. Returns null when the
    /// source has no V3 service index (e.g. local file feeds).
    /// </summary>
    private async Task<Uri?> TryBuildV3SearchUriAsync(CatalogSearchQuery query, int take, CancellationToken cancellationToken)
    {
        ServiceIndexResourceV3? serviceIndex = await _repository.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken);
        if (serviceIndex is null)
        {
            return null;
        }

        // GetServiceEntryUri picks the best match in priority order across
        // the V3 service index versions; pass the unversioned key first.
        string? baseUrl = serviceIndex.GetServiceEntryUri(
            "SearchQueryService",
            "SearchQueryService/3.5.0",
            "SearchQueryService/3.0.0-rc",
            "SearchQueryService/3.0.0-beta")?.AbsoluteUri;

        if (string.IsNullOrEmpty(baseUrl))
        {
            return null;
        }

        var qs = new List<string>
        {
            "q=" + Uri.EscapeDataString(query.Filter ?? string.Empty),
            "skip=" + query.Skip.ToString(CultureInfo.InvariantCulture),
            "take=" + take.ToString(CultureInfo.InvariantCulture),
            "prerelease=" + query.IncludePrerelease.ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            "semVerLevel=2.0.0",
            $"{PackageTypeQueryParam}=" + Uri.EscapeDataString(FuncCliWorkloadPackageType),
        };

        return new Uri(baseUrl + (baseUrl.Contains('?') ? "&" : "?") + string.Join("&", qs));
    }

    /// <summary>
    /// Parses the V3 search response and applies a defensive client-side
    /// filter on each hit's <c>packageTypes</c> array. nuget.org honours
    /// <c>packageType=</c> only when the query string is non-empty; an
    /// unfiltered <c>func workload search</c> otherwise leaks arbitrary
    /// matches like <c>Azure.Functions.Cli.Abstractions</c> (see
    /// azure-functions-core-tools#5198). Hits that omit <c>packageTypes</c>
    /// are kept, since some V3 feeds don't surface the field.
    /// </summary>
    internal static IReadOnlyList<CatalogSearchResult> ParseV3Hits(JObject response, PackageSource source)
    {
        if (response["data"] is not JArray data)
        {
            return [];
        }

        var results = new List<CatalogSearchResult>(data.Count);
        foreach (JToken hit in data)
        {
            string? id = (string?)hit["id"];
            string? versionString = (string?)hit["version"];
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(versionString) ||
                !NuGetVersion.TryParse(versionString, out NuGetVersion? version))
            {
                continue;
            }

            if (!HitMatchesWorkloadPackageType(hit["packageTypes"]))
            {
                continue;
            }

            string? tagsString = GetTagsString(hit["tags"]);
            results.Add(new CatalogSearchResult(
                PackageId: id.ToLowerInvariant(),
                LatestVersion: version,
                Title: (string?)hit["title"],
                Description: (string?)hit["description"],
                Aliases: ParseAliases(tagsString),
                Source: source)
            {
                Kind = ParseKind(tagsString),
            });
        }

        return results;
    }

    private static string? GetTagsString(JToken? tags)
    {
        // V3 search responses represent tags either as a space-delimited
        // string or as a JSON array of individual tag strings, depending
        // on the source implementation. Normalise both to the
        // space-delimited form the tag parsers expect.
        if (tags is JArray array)
        {
            return string.Join(' ', array.Select(t => (string?)t).Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        if (tags is JValue { Value: string s })
        {
            return s;
        }

        return null;
    }

    private static IReadOnlyList<string> ParseAliases(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        var aliases = new List<string>();
        foreach (string token in tags.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith(WorkloadInstaller.AliasTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string alias = token[WorkloadInstaller.AliasTagPrefix.Length..].Trim();
                if (alias.Length > 0)
                {
                    aliases.Add(alias.ToLowerInvariant());
                }
            }
        }

        return aliases;
    }

    // Last kind:<value> tag wins if a package is mis-tagged with more than one;
    // returning null when the tag is absent means callers can distinguish
    // "not declared" from a typo'd value.
    private static string? ParseKind(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return null;
        }

        string? kind = null;
        foreach (string token in tags.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith(WorkloadInstaller.KindTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string value = token[WorkloadInstaller.KindTagPrefix.Length..].Trim();
                if (value.Length > 0)
                {
                    kind = value.ToLowerInvariant();
                }
            }
        }

        return kind;
    }

    // Hits without a `packageTypes` array fall through (kept): V3 search
    // responses don't guarantee the field, and dropping silent hits would
    // empty out compliant feeds. When the array is present, require the
    // FuncCliWorkload entry so arbitrary nuget.org packages don't leak
    // through when the server-side `packageType=` filter is ignored (e.g.
    // for empty queries).
    private static bool HitMatchesWorkloadPackageType(JToken? packageTypes)
    {
        if (packageTypes is not JArray array || array.Count == 0)
        {
            return true;
        }

        foreach (JToken entry in array)
        {
            string? name = (string?)entry["name"] ?? (entry as JValue)?.Value as string;
            if (!string.IsNullOrEmpty(name)
                && string.Equals(name, FuncCliWorkloadPackageType, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
