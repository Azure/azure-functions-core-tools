// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Default <see cref="IFuncTemplateFeedSearch"/>. Local directory feeds are
/// scanned on disk (no search API); remote v3 feeds are queried through their
/// <c>SearchQueryService</c> with the func package-type filter, reusing NuGet's
/// HTTP stack so credential providers and proxy configuration apply.
/// </summary>
internal sealed class FuncTemplateFeedSearch(ILogger<FuncTemplateFeedSearch> logger) : IFuncTemplateFeedSearch
{
    internal static readonly IReadOnlyList<string> FuncTemplatePackageTypes = ["FuncItemTemplates", "FuncAppTemplates"];

    private const string PackageTypeQueryParam = "packageType";
    private const int SearchTake = 100;

    private readonly ILogger<FuncTemplateFeedSearch> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<IReadOnlyList<FuncFeedPackage>> SearchAsync(string? term, string source, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (Directory.Exists(source))
        {
            _logger.LogDebug("Scanning local directory feed {Source} for func template packages.", source);
            return ScanDirectory(source, Normalize(term));
        }

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return await SearchRemoteAsync(Normalize(term), source, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Source '{source}' is not an existing directory feed or a NuGet v3 feed URL. "
            + "Pass a local folder of .nupkg files or a v3 feed's index URL (ending in /index.json).");
    }

    private IReadOnlyList<FuncFeedPackage> ScanDirectory(string directory, string? term)
    {
        Dictionary<string, NuGetVersion> latest = new(StringComparer.OrdinalIgnoreCase);

        foreach (string nupkg in Directory.EnumerateFiles(directory, "*.nupkg", SearchOption.AllDirectories))
        {
            try
            {
                using PackageArchiveReader reader = new(nupkg);
                NuspecReader nuspec = reader.NuspecReader;

                if (!nuspec.GetPackageTypes().Any(IsFuncTemplatePackageType))
                {
                    continue;
                }

                string id = nuspec.GetId();
                if (!MatchesTerm(id, term))
                {
                    continue;
                }

                NuGetVersion version = nuspec.GetVersion();
                if (!latest.TryGetValue(id, out NuGetVersion? existing) || version > existing)
                {
                    latest[id] = version;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or PackagingException)
            {
                // Skip an unreadable/corrupt package rather than failing the whole scan.
                _logger.LogDebug(ex, "Skipping unreadable package {Path} while scanning feed.", nupkg);
            }
        }

        return Project(latest);
    }

    private async Task<IReadOnlyList<FuncFeedPackage>> SearchRemoteAsync(string? term, string source, CancellationToken cancellationToken)
    {
        SourceRepository repository = Repository.Factory.GetCoreV3(source);
        ServiceIndexResourceV3? serviceIndex = await repository.GetResourceAsync<ServiceIndexResourceV3>(cancellationToken);
        string? baseUrl = serviceIndex?.GetServiceEntryUri(
            "SearchQueryService",
            "SearchQueryService/3.5.0",
            "SearchQueryService/3.0.0-rc",
            "SearchQueryService/3.0.0-beta")?.AbsoluteUri;

        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException(
                $"Source '{source}' does not advertise a SearchQueryService entry. "
                + "Template search requires a v3 NuGet feed or a local directory feed.");
        }

        HttpSourceResource http = await repository.GetResourceAsync<HttpSourceResource>(cancellationToken);
        Dictionary<string, NuGetVersion> latest = new(StringComparer.OrdinalIgnoreCase);

        // Feeds honour a single packageType= per query, so run one query per func type and merge.
        foreach (string packageType in FuncTemplatePackageTypes)
        {
            JObject? response = await http.HttpSource.GetJObjectAsync(
                new HttpSourceRequest(BuildSearchUri(baseUrl, term, packageType), NullLogger.Instance),
                NullLogger.Instance,
                cancellationToken);

            if (response?["data"] is not JArray data)
            {
                continue;
            }

            foreach (JToken hit in data)
            {
                string? id = (string?)hit["id"];
                if (string.IsNullOrEmpty(id) || !NuGetVersion.TryParse((string?)hit["version"], out NuGetVersion? version))
                {
                    continue;
                }

                if (!HitMatchesFuncType(hit["packageTypes"]))
                {
                    continue;
                }

                if (!latest.TryGetValue(id, out NuGetVersion? existing) || version > existing)
                {
                    latest[id] = version;
                }
            }
        }

        return Project(latest);
    }

    private static IReadOnlyList<FuncFeedPackage> Project(Dictionary<string, NuGetVersion> latest)
    =>
    [
        .. latest
            .Select(kvp => new FuncFeedPackage(kvp.Key, kvp.Value.ToNormalizedString()))
            .OrderBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase),
    ];

    private static Uri BuildSearchUri(string baseUrl, string? term, string packageType)
    {
        List<string> qs =
        [
            "q=" + Uri.EscapeDataString(term ?? string.Empty),
            "skip=0",
            "take=" + SearchTake.ToString(CultureInfo.InvariantCulture),
            "prerelease=false",
            "semVerLevel=2.0.0",
            $"{PackageTypeQueryParam}=" + Uri.EscapeDataString(packageType),
        ];

        return new Uri(baseUrl + (baseUrl.Contains('?') ? "&" : "?") + string.Join("&", qs));
    }

    private static bool HitMatchesFuncType(JToken? packageTypes)
    {
        // Feeds don't guarantee the packageTypes field; keep hits that omit it,
        // but when present require a func template package type so unfiltered
        // queries don't leak arbitrary packages.
        if (packageTypes is not JArray array || array.Count == 0)
        {
            return true;
        }

        foreach (JToken entry in array)
        {
            string? name = (string?)entry["name"] ?? (entry as JValue)?.Value as string;
            if (!string.IsNullOrEmpty(name) && IsFuncTemplatePackageTypeName(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFuncTemplatePackageType(PackageType packageType)
        => IsFuncTemplatePackageTypeName(packageType.Name);

    private static bool IsFuncTemplatePackageTypeName(string name)
        => FuncTemplatePackageTypes.Any(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesTerm(string id, string? term)
        => term is null || id.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? term)
        => string.IsNullOrWhiteSpace(term) ? null : term.Trim();
}
