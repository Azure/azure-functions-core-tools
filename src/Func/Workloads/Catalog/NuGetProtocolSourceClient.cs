// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
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
        JObject? raw = await FetchPageResponseAsync(query, cancellationToken);
        return raw is null ? [] : ParseV3Hits(raw, Source);
    }

    public async Task<CatalogSearchPage> SearchPageAsync(CatalogSearchQuery query, CancellationToken cancellationToken)
    {
        JObject? raw;
        try
        {
            raw = await FetchPageResponseAsync(query, cancellationToken);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new InvalidDataException("The workload source returned malformed search response JSON.", ex);
        }

        if (raw?["data"] is not JArray data)
        {
            throw new InvalidDataException("The workload search response must contain a data array.");
        }

        int rawCount = data.Count;
        JArray workloadRows = FilterAndValidateDiscoveryRows(data);
        long? total = null;
        if (raw.TryGetValue("totalHits", out JToken? totalToken))
        {
            if (totalToken.Type != JTokenType.Integer || !long.TryParse(totalToken.ToString(), out long count) || count < 0)
                throw new InvalidDataException("The workload search totalHits must be a non-negative integer.");
            total = count;
        }

        return new CatalogSearchPage(ParseV3Hits(new JObject { ["data"] = workloadRows }, Source), rawCount, total);
    }

    private async Task<JObject?> FetchPageResponseAsync(CatalogSearchQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        int take = query.Take ?? CatalogSearchQuery.DefaultTake;
        Uri? searchUri = await TryBuildV3SearchUriAsync(query, take, cancellationToken);

        if (searchUri is null)
        {
            throw new InvalidWorkloadSourceException(
                $"Source '{Source.Source}' does not advertise a SearchQueryService entry. Workload search requires a V3 NuGet feed.");
        }

        return await FetchSearchResponseAsync(searchUri, cancellationToken);
    }

    public async Task<IReadOnlyList<NuGetVersion>> ListVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        FindPackageByIdResource findResource = await GetIndexDependentResourceAsync<FindPackageByIdResource>(cancellationToken);
        using var cache = new SourceCacheContext();

        IEnumerable<NuGetVersion> versions = await findResource.GetAllVersionsAsync(packageId, cache, NullLogger.Instance, cancellationToken);

        return versions?.ToList() ?? [];
    }

    public async Task<Stream> OpenPackageAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        FindPackageByIdResource findResource = await GetIndexDependentResourceAsync<FindPackageByIdResource>(cancellationToken);
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
                packageId, version, fileStream, cache, NullLogger.Instance, cancellationToken);

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
            new HttpSourceRequest(searchUri, NullLogger.Instance), NullLogger.Instance, cancellationToken);
    }

    /// <summary>
    /// Resolves the source's <c>SearchQueryService</c> entry from its V3
    /// service index and builds a request URL with the
    /// <c>packageType=FuncCliWorkload</c> filter. Returns null when the
    /// source has no V3 service index (e.g. local file feeds).
    /// </summary>
    private async Task<Uri?> TryBuildV3SearchUriAsync(CatalogSearchQuery query, int take, CancellationToken cancellationToken)
    {
        ServiceIndexResourceV3? serviceIndex = await GetIndexDependentResourceAsync<ServiceIndexResourceV3>(cancellationToken);
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
            "prerelease=" + (query.IncludePrerelease ?? false).ToString(CultureInfo.InvariantCulture).ToLowerInvariant(),
            "semVerLevel=2.0.0",
            $"{PackageTypeQueryParam}=" + Uri.EscapeDataString(WorkloadPackageTypes.Workload),
        };

        return new Uri(baseUrl + (baseUrl.Contains('?') ? "&" : "?") + string.Join("&", qs));
    }

    private async Task<T> GetIndexDependentResourceAsync<T>(CancellationToken cancellationToken) where T : class, INuGetResource
    {
        try
        {
            return await _repository.GetResourceAsync<T>(cancellationToken);
        }
        catch (FatalProtocolException ex) when (HasMalformedServiceIndexCause(ex))
        {
            throw new InvalidWorkloadSourceException(
                "The workload source returned a malformed V3 service index. Correct the feed response or select another --source.", ex);
        }
    }

    /// <summary>
    /// Parses the V3 search response and applies a defensive client-side
    /// filter on each hit's <c>packageTypes</c> array. The filter is belt and
    /// braces for feeds that ignore <c>packageType=</c>; nuget.org applies it
    /// server-side even for an empty query (re-measured 2026-08-19: 21 of 21
    /// hits were workloads). Hits that omit <c>packageTypes</c> are kept, since
    /// some V3 feeds don't surface the field.
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
                Title: (hit["title"] as JValue)?.Value as string,
                Description: (hit["description"] as JValue)?.Value as string,
                Aliases: WorkloadPackageTags.ParseValues(tagsString, WorkloadPackageTags.AliasPrefix),
                Source: source)
            {
                Kind = ParseKind(tagsString),
                Rid = ParseRid(tagsString),
                CanonicalStack = ParseCanonicalStack(tagsString),
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

    // Last kind:<value> tag wins if a package is mis-tagged with more than one;
    // returning null when the tag is absent means callers can distinguish
    // "not declared" from a typo'd value.
    private static string? ParseKind(string? tags)
        => WorkloadPackageTags.ParseLastValue(tags, WorkloadPackageTags.KindPrefix);

    private static string? ParseCanonicalStack(string? tags)
    {
        string[] declarations = [.. (tags ?? string.Empty).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => tag.StartsWith(WorkloadPackageTags.StackPrefix, StringComparison.OrdinalIgnoreCase))];
        // Empty and duplicate declarations are invalid, not the same as omission.
        return declarations.Length == 0 ? null
            : declarations.Length == 1 ? declarations[0][WorkloadPackageTags.StackPrefix.Length..].ToLowerInvariant()
            : string.Join(' ', declarations.Select(tag => tag[WorkloadPackageTags.StackPrefix.Length..].ToLowerInvariant()));
    }

    private static bool HasMalformedServiceIndexCause(Exception exception)
    {
        for (Exception? cause = exception.InnerException; cause is not null; cause = cause.InnerException)
        {
            // NuGet wraps both JSON parsing and service-index token conversion failures.
            if (cause is InvalidDataException or Newtonsoft.Json.JsonException
                or ArgumentException or InvalidOperationException or FormatException or InvalidCastException or OverflowException)
                return true;
        }

        return false;
    }

    private static JArray FilterAndValidateDiscoveryRows(JArray rows)
    {
        JArray workloadRows = [];
        foreach (JToken token in rows)
        {
            if (token is not JObject row)
                throw new InvalidDataException("The workload search response contains an invalid package identity or version.");

            JToken? packageTypes = row["packageTypes"];
            if (packageTypes is not null && packageTypes.Type != JTokenType.Null)
            {
                if (packageTypes is not JArray types || types.Any(type =>
                    (type is JObject entry ? entry["name"] : type) is not JValue { Type: JTokenType.String } name
                    || string.IsNullOrWhiteSpace((string?)name)))
                    throw new InvalidDataException("The workload search packageTypes must contain package type names.");
            }

            // Only a valid type declaration can rule out a workload before identity and ownership validation.
            if (!HitMatchesWorkloadPackageType(packageTypes))
                continue;

            if (row["id"] is not JValue { Type: JTokenType.String } id || string.IsNullOrWhiteSpace((string?)id)
                || row["version"] is not JValue { Type: JTokenType.String } version || !NuGetVersion.TryParse((string?)version, out _))
                throw new InvalidDataException("The workload search response contains an invalid package identity or version.");

            JToken? tags = row["tags"];
            if (tags is not null && tags.Type != JTokenType.Null && tags.Type != JTokenType.String
                && (tags is not JArray tagArray || tagArray.Any(tag => tag.Type != JTokenType.String)))
                throw new InvalidDataException("The workload search tags must be a string or an array of strings.");

            string? tagText = GetTagsString(tags);
            foreach (string prefix in new[] { WorkloadPackageTags.KindPrefix, WorkloadPackageTags.RuntimeIdentifierPrefix })
            {
                string[] declarations = [.. (tagText ?? string.Empty).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))];
                if (declarations.Length > 1 || declarations.Any(tag => tag.Length == prefix.Length))
                    throw new InvalidDataException($"The workload search response has an invalid '{prefix}' declaration.");
            }

            workloadRows.Add(row);
        }

        return workloadRows;
    }

    // Last rid:<value> tag wins, same as kind. Per-RID workload packs (host,
    // python worker) carry exactly one `rid:` tag matching the runtime they
    // target; single-RID packs omit it.
    private static string? ParseRid(string? tags)
        => WorkloadPackageTags.ParseLastValue(tags, WorkloadPackageTags.RuntimeIdentifierPrefix);

    // Hits without a `packageTypes` array fall through (kept): V3 search
    // responses don't guarantee the field, and dropping silent hits would
    // empty out compliant feeds. When the array is present, require the
    // FuncCliWorkload entry so arbitrary packages don't leak through from a
    // feed that ignores the server-side `packageType=` filter.
    private static bool HitMatchesWorkloadPackageType(JToken? packageTypes)
    {
        if (packageTypes is not JArray array)
        {
            return true;
        }

        foreach (JToken entry in array)
        {
            string? name = entry is JObject type ? (string?)type["name"] : (entry as JValue)?.Value as string;
            if (!string.IsNullOrEmpty(name)
                && string.Equals(name, WorkloadPackageTypes.Workload, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
