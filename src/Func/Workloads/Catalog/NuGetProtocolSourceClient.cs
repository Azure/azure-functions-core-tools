// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Common;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// HTTP-backed <see cref="ISourceClient"/> built on <c>NuGet.Protocol</c>.
/// Drives <see cref="PackageSearchResource"/> and
/// <see cref="FindPackageByIdResource"/> against the v3 service index of
/// the supplied <see cref="PackageSource"/>, restricting search to the
/// <c>FuncCliWorkload</c> package type.
/// </summary>
internal sealed class NuGetProtocolSourceClient(PackageSource source, SourceRepository repository) : ISourceClient
{
    private const string PackageType = "FuncCliWorkload";
    private const string AliasTagPrefix = "alias:";

    private readonly SourceRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public PackageSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        string? query,
        bool includePrerelease,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        PackageSearchResource searchResource = await _repository.GetResourceAsync<PackageSearchResource>(cancellationToken);

        SearchFilter filter = new(includePrerelease)
        {
            PackageTypes = [PackageType],
        };

        IEnumerable<IPackageSearchMetadata> hits = await searchResource.SearchAsync(
            query ?? string.Empty,
            filter,
            skip,
            take,
            NullLogger.Instance,
            cancellationToken);

        var results = new List<CatalogSearchResult>();
        foreach (IPackageSearchMetadata hit in hits)
        {
            if (hit.Identity?.Id is null || hit.Identity.Version is null)
            {
                continue;
            }

            results.Add(new CatalogSearchResult(
                PackageId: hit.Identity.Id.ToLowerInvariant(),
                LatestVersion: hit.Identity.Version,
                Title: hit.Title,
                Description: hit.Description,
                Aliases: ParseAliases(hit.Tags),
                Source: Source));
        }

        return results;
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
        // internal buffers alive.
        string tempPath = Path.Combine(Path.GetTempPath(), $"funccli-workload-{Guid.NewGuid():N}.nupkg");
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
                await fileStream.DisposeAsync();
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
}
