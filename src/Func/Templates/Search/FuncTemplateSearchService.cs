// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Default <see cref="IFuncTemplateSearchService"/>: resolves the func index
/// (or a <c>--source</c> feed), matches packages by term, and annotates each
/// result with its installed state.
/// </summary>
internal sealed class FuncTemplateSearchService(
    IFuncSearchIndexProvider indexProvider,
    IFuncTemplateFeedSearch feedSearch,
    IFuncTemplatePackageService packageService,
    ILogger<FuncTemplateSearchService> logger) : IFuncTemplateSearchService
{
    // Full listings (empty term) can be large; keep terminal output bounded.
    private const int MaxListedPackages = 100;

    private readonly IFuncSearchIndexProvider _indexProvider = indexProvider ?? throw new ArgumentNullException(nameof(indexProvider));
    private readonly IFuncTemplateFeedSearch _feedSearch = feedSearch ?? throw new ArgumentNullException(nameof(feedSearch));
    private readonly IFuncTemplatePackageService _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
    private readonly ILogger<FuncTemplateSearchService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<FuncSearchResults> SearchAsync(FuncSearchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? term = string.IsNullOrWhiteSpace(request.Term) ? null : request.Term.Trim();
        IReadOnlyList<InstalledTemplatePackage> installed = await _packageService.ListInstalledAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(request.Source)
            ? await SearchIndexAsync(term, installed, cancellationToken)
            : await SearchFeedAsync(term, request.Source, installed, cancellationToken);
    }

    private async Task<FuncSearchResults> SearchIndexAsync(string? term, IReadOnlyList<InstalledTemplatePackage> installed, CancellationToken cancellationToken)
    {
        FuncSearchIndex index = await _indexProvider.GetIndexAsync(cancellationToken);
        _logger.LogDebug("Searching template index with {PackageCount} packages for term {Term}", index.Packages.Count, term ?? "(all)");

        List<FuncSearchPackageResult> results = [];
        foreach (FuncSearchPackage package in index.Packages)
        {
            List<FuncSearchTemplate> matchedTemplates = term is null
                ? [.. package.Templates]
                : [.. package.Templates.Where(t => TemplateMatches(t, term))];

            bool packageMatches = term is null || PackageIdMatches(package, term) || matchedTemplates.Count > 0;
            if (!packageMatches)
            {
                continue;
            }

            IReadOnlyList<FuncSearchTemplate> display = matchedTemplates.Count > 0 ? matchedTemplates : package.Templates;
            results.Add(new FuncSearchPackageResult(
                package.Name,
                package.Version,
                [.. display.Select(Project)],
                DetermineInstalledState(package.Name, package.Version, installed)));
        }

        IReadOnlyList<FuncSearchPackageResult> ordered =
        [
            .. results.OrderBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase).Take(MaxListedPackages),
        ];
        return new FuncSearchResults(term, Source: null, ordered);
    }

    private async Task<FuncSearchResults> SearchFeedAsync(string? term, string source, IReadOnlyList<InstalledTemplatePackage> installed, CancellationToken cancellationToken)
    {
        IReadOnlyList<FuncFeedPackage> feedPackages = await _feedSearch.SearchAsync(term, source, cancellationToken);
        _logger.LogDebug("Feed {Source} returned {PackageCount} func template packages", source, feedPackages.Count);

        IReadOnlyList<FuncSearchPackageResult> ordered =
        [
            .. feedPackages
                .OrderBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxListedPackages)
                .Select(p => new FuncSearchPackageResult(
                    p.PackageId,
                    p.Version,
                    Templates: [],
                    DetermineInstalledState(p.PackageId, p.Version, installed))),
        ];
        return new FuncSearchResults(term, source, ordered);
    }

    private static bool PackageIdMatches(FuncSearchPackage package, string term)
        => Contains(package.Name, term) || Contains(package.Description, term);

    private static bool TemplateMatches(FuncSearchTemplate template, string term)
        => Contains(template.Name, term)
        || Contains(template.Identity, term)
        || Contains(template.Description, term)
        || Contains(template.Author, term)
        || template.ShortNameList.Any(s => Contains(s, term))
        || template.Classifications.Any(c => Contains(c, term))
        || template.Tags.Values.Any(v => Contains(v, term))
        || template.Tags.Keys.Any(k => Contains(k, term));

    private static bool Contains(string? value, string term)
        => value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static FuncSearchTemplateResult Project(FuncSearchTemplate template)
    {
        template.Tags.TryGetValue(FuncTemplateTags.Stack, out string? stack);
        template.Tags.TryGetValue(FuncTemplateTags.Language, out string? language);
        return new FuncSearchTemplateResult(
            string.IsNullOrWhiteSpace(template.Name) ? template.Identity : template.Name,
            template.ShortNameList,
            string.IsNullOrWhiteSpace(stack) ? null : stack,
            string.IsNullOrWhiteSpace(language) ? null : language);
    }

    private static FuncTemplateInstalledState DetermineInstalledState(string packageId, string? indexVersion, IReadOnlyList<InstalledTemplatePackage> installed)
    {
        InstalledTemplatePackage? match = installed.FirstOrDefault(
            p => string.Equals(p.Identifier, packageId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return new FuncTemplateInstalledState.NotInstalled();
        }

        if (!string.IsNullOrWhiteSpace(indexVersion)
            && NuGetVersion.TryParse(match.Version, out NuGetVersion? installedVersion)
            && NuGetVersion.TryParse(indexVersion, out NuGetVersion? availableVersion)
            && availableVersion > installedVersion)
        {
            return new FuncTemplateInstalledState.UpdateAvailable(match.Version, indexVersion);
        }

        return new FuncTemplateInstalledState.Installed(match.Version);
    }
}
