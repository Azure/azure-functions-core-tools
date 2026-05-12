// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Common;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// HTTP- or file-backed client built on <c>NuGet.Protocol</c>. Drives
/// <see cref="PackageSearchResource"/> and <see cref="FindPackageByIdResource"/>
/// against the v3 service index (remote) or the on-disk feed layout (local)
/// of the supplied <see cref="SourceRepository"/>, restricting search to the
/// <c>FuncCliWorkload</c> package type.
/// </summary>
internal sealed class NuGetProtocolSourceClient(SourceRepository repository)
{
    private const string PackageType = "FuncCliWorkload";
    private const string AliasTagPrefix = "alias:";

    private readonly SourceRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public PackageSource Source => _repository.PackageSource;

    /// <summary>
    /// Returns an <see cref="AsyncPageable{T}"/> over the search results for
    /// this source. Paging is driven by NuGet's <c>skip</c>/<c>take</c>; the
    /// continuation token encodes the next skip offset.
    /// </summary>
    public AsyncPageable<CatalogSearchResult> Search(
        CatalogSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new SearchPageable(_repository, Source, query, cancellationToken);
    }

    public async Task<IReadOnlyList<NuGetVersion>> ListVersionsAsync(
        string packageId,
        CancellationToken cancellationToken)
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

    public async Task<Stream> OpenPackageAsync(
        string packageId,
        NuGetVersion version,
        CancellationToken cancellationToken)
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

    internal static CatalogSearchResult? ToResult(IPackageSearchMetadata hit, PackageSource source)
    {
        if (hit.Identity?.Id is null || hit.Identity.Version is null)
        {
            return null;
        }

        return new CatalogSearchResult(
            PackageId: hit.Identity.Id.ToLowerInvariant(),
            LatestVersion: hit.Identity.Version,
            Title: hit.Title,
            Description: hit.Description,
            Aliases: ParseAliases(hit.Tags),
            Source: source);
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
            if (token.StartsWith(AliasTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string alias = token[AliasTagPrefix.Length..].Trim();
                if (alias.Length > 0)
                {
                    aliases.Add(alias.ToLowerInvariant());
                }
            }
        }

        return aliases;
    }

    private sealed class SearchPageable(
        SourceRepository repository,
        PackageSource source,
        CatalogSearchQuery query,
        CancellationToken cancellationToken) : AsyncPageable<CatalogSearchResult>(cancellationToken)
    {
        private readonly SourceRepository _repository = repository;
        private readonly PackageSource _source = source;
        private readonly CatalogSearchQuery _query = query;

        public override async IAsyncEnumerable<Page<CatalogSearchResult>> AsPages(
            string? continuationToken = null,
            int? pageSizeHint = null)
        {
            int pageSize = pageSizeHint
                ?? _query.PageSize
                ?? CatalogSearchQuery.DefaultPageSize;
            int skip = int.TryParse(continuationToken ?? _query.ContinuationToken, out int parsed) ? parsed : 0;

            CancellationToken ct = CancellationToken;
            PackageSearchResource searchResource = await _repository.GetResourceAsync<PackageSearchResource>(ct);
            SearchFilter filter = new(_query.IncludePrerelease)
            {
                PackageTypes = [PackageType],
            };

            while (true)
            {
                IEnumerable<IPackageSearchMetadata> hits = await searchResource.SearchAsync(
                    _query.Filter ?? string.Empty,
                    filter,
                    skip,
                    pageSize,
                    NullLogger.Instance,
                    ct);

                var values = new List<CatalogSearchResult>();
                foreach (IPackageSearchMetadata hit in hits)
                {
                    CatalogSearchResult? result = ToResult(hit, _source);
                    if (result is not null)
                    {
                        values.Add(result);
                    }
                }

                // A short page ends the stream. v3 doesn't expose a totalHits
                // we can trust, so we infer end-of-stream from page size.
                string? nextToken = values.Count < pageSize ? null : (skip + pageSize).ToString();
                yield return Page<CatalogSearchResult>.FromValues(values, nextToken, response: null!);

                if (nextToken is null)
                {
                    yield break;
                }

                skip += pageSize;
            }
        }
    }
}
