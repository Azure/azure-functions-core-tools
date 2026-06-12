// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload search [&lt;query&gt;]</c>. Renders matching workload
/// packages from the configured catalog as a stack of definition-list
/// "cards", one per package, so that long descriptions and package ids
/// flow naturally across the terminal width instead of wrapping inside
/// narrow table cells.
/// </summary>
internal sealed class WorkloadSearchCommand : FuncCliCommand
{
    private const int DefaultTake = 20;

    // Lowercased value of the `kind:` NuGet tag that marks a package as a stack
    // workload (the only kind `func init --stack` can target). Mirrors the
    // `kind:workload` PackageTag stack csprojs emit.
    private const string StackKind = "workload";

    // Label used for non-prerelease versions when grouping search results by
    // release channel. Versions without a prerelease label all collapse to
    // this single bucket so the stable channel renders as one row.
    private const string StableChannel = "stable";

    private readonly IInteractionService _interaction;
    private readonly IWorkloadCatalog _catalog;
    private readonly WorkloadCatalogOptions _catalogOptions;

    public Argument<string?> QueryArgument { get; } = new("query")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "Optional free-form query. Omit to list all workloads.",
    };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "NuGet feed URI to search.",
    };

    public Option<bool?> IncludePrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Include prerelease versions in the results. Default: stable when running a stable CLI build, prerelease when running a prerelease CLI build.",
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Emit machine-readable JSON instead of cards.",
    };

    public Option<bool> StackOption { get; } = new("--stack")
    {
        Description = "Show only stack workloads (packages tagged 'kind:workload', e.g. dotnet, node, python).",
    };

    public WorkloadSearchCommand(IInteractionService interaction, IWorkloadCatalog catalog, IOptions<WorkloadCatalogOptions> catalogOptions)
        : base("search", "Search the workload catalog.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _catalogOptions = catalogOptions?.Value ?? throw new ArgumentNullException(nameof(catalogOptions));

        Arguments.Add(QueryArgument);
        Options.Add(SourceOption);
        Options.Add(IncludePrereleaseOption);
        Options.Add(JsonOption);
        Options.Add(StackOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? query = parseResult.GetValue(QueryArgument);
        string? source = parseResult.GetValue(SourceOption);
        bool? includePrerelease = parseResult.GetValue(IncludePrereleaseOption);
        bool json = parseResult.GetValue(JsonOption);
        bool stackOnly = parseResult.GetValue(StackOption);

        bool effectivePrerelease = includePrerelease ?? _catalogOptions.IncludePrerelease;

        if (effectivePrerelease && !json)
        {
            _interaction.WriteHint(WorkloadInstallCommand.PrereleasePreviewHint);
        }

        // TODO: surface --skip and --take for paging once we have a UX
        // story for repeated queries (see workload spec §4.1).
        var searchQuery = new CatalogSearchQuery
        {
            Filter = query,
            IncludePrerelease = effectivePrerelease,
            Take = DefaultTake,
            Source = source,
        };

        IReadOnlyList<CatalogSearchResult> results;
        try
        {
            results = await _catalog.SearchAsync(searchQuery, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }

        if (stackOnly)
        {
            results = [.. results.Where(r => string.Equals(r.Kind, StackKind, StringComparison.OrdinalIgnoreCase))];
        }

        // Track the number of distinct packages the catalog returned before
        // we expand into per-channel rows, so the "more may be available"
        // hint stays anchored to the search page size rather than the
        // post-expansion row count.
        int packageCount = results.Count;

        if (effectivePrerelease && results.Count > 0)
        {
            results = await ExpandResultsByChannelAsync(results, cancellationToken);
        }

        if (json)
        {
            _interaction.WriteJson(results.Select(r => new
            {
                packageId = r.PackageId,
                latestVersion = r.LatestVersion.ToNormalizedString(),
                channel = r.Channel,
                title = r.Title,
                description = r.Description,
                aliases = r.Aliases,
            }));
            return 0;
        }

        if (results.Count == 0)
        {
            _interaction.WriteWarning("No workloads found.");
            return 0;
        }

        var card = new WorkloadCardWriter(_interaction);
        bool first = true;
        foreach (CatalogSearchResult result in results)
        {
            if (!first)
            {
                card.WriteSeparator();
            }

            first = false;
            WriteCard(card, result);
        }

        WriteFooter(results.Count, packageCount);
        return 0;
    }

    /// <summary>
    /// Returns the channel a version belongs to. Non-prerelease versions all
    /// fall in <c>stable</c>; prerelease versions take the first release
    /// label (e.g. <c>4.42.0-preview.1</c> → <c>preview</c>,
    /// <c>5.0.0-experimental</c> → <c>experimental</c>). Any post-dash label
    /// becomes its own channel, so the command isn't limited to a hard-coded
    /// preview/experimental list.
    /// </summary>
    internal static string GetChannel(NuGetVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (!version.IsPrerelease)
        {
            return StableChannel;
        }

        foreach (string label in version.ReleaseLabels)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                return label.ToLowerInvariant();
            }
        }

        return StableChannel;
    }

    /// <summary>
    /// Expands each search hit into one row per release channel, fetching
    /// the full version list per package and picking the latest version in
    /// each channel. Preserves the original package ordering returned by
    /// the catalog; within a package, channels are sorted by latest version
    /// descending so the newest channel renders first.
    /// </summary>
    private async Task<IReadOnlyList<CatalogSearchResult>> ExpandResultsByChannelAsync(
        IReadOnlyList<CatalogSearchResult> results,
        CancellationToken cancellationToken)
    {
        CatalogSearchResult[][] perPackage = await Task.WhenAll(results.Select(r => ExpandSingleAsync(r, cancellationToken)));
        return [.. perPackage.SelectMany(x => x)];
    }

    private async Task<CatalogSearchResult[]> ExpandSingleAsync(CatalogSearchResult result, CancellationToken cancellationToken)
    {
        IReadOnlyList<NuGetVersion> versions;
        try
        {
            versions = await _catalog.ListVersionsAsync(result.PackageId, result.Source.Source, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Version listing failures shouldn't drop the package from
            // search output, the user can still see and install the row
            // they already had. Fall back to the original hit, just
            // tagged with the channel of the version we already know.
            return [result with { Channel = GetChannel(result.LatestVersion) }];
        }

        if (versions.Count == 0)
        {
            return [result with { Channel = GetChannel(result.LatestVersion) }];
        }

        return [.. versions
            .GroupBy(GetChannel, StringComparer.OrdinalIgnoreCase)
            .Select(g => result with { LatestVersion = g.Max()!, Channel = g.Key })
            .OrderByDescending(r => r.LatestVersion)];
    }

    private static void WriteCard(WorkloadCardWriter card, CatalogSearchResult result)
    {
        card.WriteHeading(DisplayNameOrPackageId(result));
        card.WriteField("Version", result.LatestVersion.ToNormalizedString());
        if (!string.IsNullOrEmpty(result.Channel))
        {
            card.WriteField("Channel", result.Channel);
        }

        card.WriteField("Package ID", result.PackageId);
        card.WriteAliases(result.Aliases);
        card.WriteDescription(result.Description);
    }

    private void WriteFooter(int count, int packageCount)
    {
        _interaction.WriteBlankLine();
        string countLine = $"Showing {count} {(count == 1 ? "result" : "results")}.";
        if (packageCount >= DefaultTake)
        {
            countLine += " More may be available, refine your query.";
        }

        _interaction.WriteHint(countLine);
        _interaction.WriteHint("Run 'func workload install <alias>' to install one.");
    }

    private static string DisplayNameOrPackageId(CatalogSearchResult result)
        => string.IsNullOrWhiteSpace(result.Title) ? result.PackageId : result.Title!;
}
