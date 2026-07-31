// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/PackChecking/PackSourceChecker.cs
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

using System.Text.Json.Nodes;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Orchestrates a single index-build run: enumerate candidate packages, apply the incremental (<c>--diff</c>)
/// carry-over and the <c>template.json</c> prefilter, scan survivors with the real engine, and write the
/// ver2 search cache plus the non-template skip-list.
/// </summary>
internal sealed class DiscoveryRunner(PackageScanner scanner, SearchCacheStore store)
{
    private const string NoTemplateJsonReason = "Package did not contain any template.json files";
    private const string ScanFoundNoTemplatesReason = "Failed to scan the package for templates, the package may contain invalid templates.";

    private readonly PackageScanner _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
    private readonly SearchCacheStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<int> RunAsync(DiscoveryOptions options, IPackageProvider provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(provider);

        string cacheDirectory = Path.Combine(options.OutputPath.FullName, SearchCacheStore.CacheContentDirectory);
        IReadOnlyDictionary<string, JsonObject> existingIndex = options.Diff
            ? _store.ReadExistingIndex(Path.Combine(cacheDirectory, SearchCacheStore.SearchMetadataFileName))
            : new Dictionary<string, JsonObject>();
        IReadOnlyDictionary<string, FilteredPackage> knownNonTemplatePacks = options.Diff
            ? _store.ReadNonTemplatePacks(Path.Combine(cacheDirectory, SearchCacheStore.NonTemplatePacksFileName))
            : new Dictionary<string, FilteredPackage>();

        var packages = new List<JsonObject>();
        var filtered = new List<FilteredPackage>();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int scannedCount = 0, carriedOverCount = 0, templatePackCount = 0;

        Console.WriteLine($"Building template search index from provider '{provider.Name}'.");

        try
        {
            await foreach (CandidatePackage candidate in provider.GetCandidatesAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!processed.Add(candidate.Name))
                {
                    LogVerbose(options, $"Skipping {candidate.Name}@{candidate.Version}: already processed a version of this package.");
                    continue;
                }

                if (TryCarryOver(options, candidate, existingIndex, knownNonTemplatePacks, packages, filtered, out bool wasTemplatePack))
                {
                    carriedOverCount++;
                    if (wasTemplatePack)
                    {
                        templatePackCount++;
                    }

                    continue;
                }

                string? localPath = await provider.EnsureLocalAsync(candidate, cancellationToken);
                if (localPath is null)
                {
                    Console.WriteLine($"[warn] Could not obtain package {candidate.Name}@{candidate.Version}; it will be skipped.");
                    continue;
                }

                if (!options.NoTemplateJsonFilter && !_scanner.ContainsTemplateJson(localPath))
                {
                    LogVerbose(options, $"Filtering {candidate.Name}@{candidate.Version}: no template.json.");
                    filtered.Add(new FilteredPackage(candidate.Name, candidate.Version, NoTemplateJsonReason));
                    continue;
                }

                scannedCount++;
                IReadOnlyList<ITemplateInfo> templates = await _scanner.ScanAsync(candidate.Name, localPath, cancellationToken);
                if (templates.Count == 0)
                {
                    filtered.Add(new FilteredPackage(candidate.Name, candidate.Version, ScanFoundNoTemplatesReason));
                    continue;
                }

                LogVerbose(options, $"Indexed {candidate.Name}@{candidate.Version}: {templates.Count} template(s).");
                packages.Add(_store.BuildPackageObject(candidate, templates));
                templatePackCount++;
            }
        }
        finally
        {
            provider.CleanupDownloads();
        }

        packages.Sort((a, b) => string.Compare(a["Name"]?.GetValue<string>(), b["Name"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase));
        _store.Write(options.OutputPath.FullName, packages, filtered);

        Console.WriteLine(
            $"Index build complete: {templatePackCount} template package(s) ({scannedCount} scanned, {carriedOverCount} carried over), {filtered.Count} non-template package(s).");
        Console.WriteLine($"Output: {Path.Combine(cacheDirectory, SearchCacheStore.SearchMetadataFileName)}");
        return 0;
    }

    private bool TryCarryOver(
        DiscoveryOptions options,
        CandidatePackage candidate,
        IReadOnlyDictionary<string, JsonObject> existingIndex,
        IReadOnlyDictionary<string, FilteredPackage> knownNonTemplatePacks,
        List<JsonObject> packages,
        List<FilteredPackage> filtered,
        out bool wasTemplatePack)
    {
        wasTemplatePack = false;
        if (!options.Diff)
        {
            return false;
        }

        if (existingIndex.TryGetValue(candidate.Name, out JsonObject? existing)
            && string.Equals(existing["Version"]?.GetValue<string>(), candidate.Version, StringComparison.OrdinalIgnoreCase))
        {
            LogVerbose(options, $"Carrying over unchanged package {candidate.Name}@{candidate.Version}.");
            packages.Add(existing.DeepClone().AsObject());
            wasTemplatePack = true;
            return true;
        }

        if (knownNonTemplatePacks.TryGetValue(candidate.Name, out FilteredPackage? known)
            && string.Equals(known.Version, candidate.Version, StringComparison.OrdinalIgnoreCase))
        {
            LogVerbose(options, $"Carrying over known non-template package {candidate.Name}@{candidate.Version}.");
            filtered.Add(known);
            return true;
        }

        return false;
    }

    private static void LogVerbose(DiscoveryOptions options, string message)
    {
        if (options.Verbose)
        {
            Console.WriteLine(message);
        }
    }
}
