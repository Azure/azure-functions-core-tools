// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Packaging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Local-directory <see cref="ISourceClient"/>: serves <c>.nupkg</c>s from
/// <see cref="PackageSource.Location"/>, filtered to the <c>FuncCliWorkload</c> package type.
/// </summary>
internal sealed class LocalFolderSourceClient(PackageSource source) : ISourceClient
{
    private const string PackageType = "FuncCliWorkload";
    private const string AliasTagPrefix = "alias:";

    public PackageSource Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    public Task<IReadOnlyList<CatalogSearchResult>> SearchAsync(
        string? query,
        bool includePrerelease,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        IEnumerable<PackageMetadata> packages = EnumerateMetadata(cancellationToken);

        var byId = new Dictionary<string, CatalogSearchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (PackageMetadata p in packages)
        {
            if (!includePrerelease && p.Version.IsPrerelease)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(query) && !MatchesQuery(p, query))
            {
                continue;
            }

            if (byId.TryGetValue(p.Id, out CatalogSearchResult? existing) && existing.LatestVersion >= p.Version)
            {
                continue;
            }

            byId[p.Id] = new CatalogSearchResult(
                PackageId: p.Id,
                LatestVersion: p.Version,
                Title: p.Title,
                Description: p.Description,
                Aliases: p.Aliases,
                Source: Source);
        }

        IReadOnlyList<CatalogSearchResult> ordered = byId.Values
            .OrderByDescending(r => r.LatestVersion)
            .ThenBy(r => r.PackageId, StringComparer.Ordinal)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult(ordered);
    }

    public Task<IReadOnlyList<NuGetVersion>> ListVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        IReadOnlyList<NuGetVersion> versions = EnumerateMetadata(cancellationToken)
            .Where(p => string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Version)
            .Distinct()
            .OrderBy(v => v)
            .ToList();

        return Task.FromResult(versions);
    }

    public Task<Stream> OpenPackageAsync(string packageId, NuGetVersion version, CancellationToken cancellationToken)
    {
        foreach (PackageMetadata p in EnumerateMetadata(cancellationToken))
        {
            if (string.Equals(p.Id, packageId, StringComparison.OrdinalIgnoreCase) && p.Version == version)
            {
                Stream stream = File.OpenRead(p.Path);
                return Task.FromResult(stream);
            }
        }

        throw new WorkloadPackageNotFoundException(
            $"Package '{packageId}' {version} was not found on source '{Source.Name}'.");
    }

    private static bool MatchesQuery(PackageMetadata p, string query)
    {
        if (p.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(p.Title) && p.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string alias in p.Aliases)
        {
            if (alias.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<PackageMetadata> EnumerateMetadata(CancellationToken cancellationToken)
    {
        string root = Source.Location.LocalPath;
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(root, "*.nupkg", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            PackageMetadata? metadata;
            try
            {
                metadata = ReadMetadata(file);
            }
            catch
            {
                // Best-effort scan of the directory; a malformed nupkg shouldn't
                // poison search results. Skip and continue.
                continue;
            }

            if (metadata is not null)
            {
                yield return metadata;
            }
        }
    }

    private static PackageMetadata? ReadMetadata(string path)
    {
        using PackageArchiveReader reader = new(File.OpenRead(path));
        NuspecReader nuspec = reader.NuspecReader;

        if (!nuspec.GetPackageTypes().Any(t => string.Equals(t.Name, PackageType, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return new PackageMetadata(
            Id: nuspec.GetId().ToLowerInvariant(),
            Version: nuspec.GetVersion(),
            Title: SafeTitle(nuspec),
            Description: SafeDescription(nuspec),
            Aliases: ParseAliases(nuspec.GetTags()),
            Path: path);
    }

    private static string? SafeTitle(NuspecReader nuspec)
    {
        try
        {
            string title = nuspec.GetTitle();
            return string.IsNullOrWhiteSpace(title) ? null : title;
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeDescription(NuspecReader nuspec)
    {
        try
        {
            string description = nuspec.GetDescription();
            return string.IsNullOrWhiteSpace(description) ? null : description;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ParseAliases(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        var aliases = new List<string>();
        foreach (string token in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
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

    private sealed record PackageMetadata(
        string Id,
        NuGetVersion Version,
        string? Title,
        string? Description,
        IReadOnlyList<string> Aliases,
        string Path);
}
