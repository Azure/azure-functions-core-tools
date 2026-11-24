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
    private const int PageSize = 20;

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
        Description = "Show only packages that declare a project stack.",
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

        CatalogSearchQuery searchQuery = new()
        {
            Filter = query,
            IncludePrerelease = effectivePrerelease,
            Take = PageSize,
            Source = source,
        };

        IReadOnlyList<CatalogSearchResult> results;
        try
        {
            results = await _catalog.SearchVersionsAsync(searchQuery, cancellationToken);
        }
        catch (WorkloadCatalogException ex)
        {
            throw new GracefulException($"Workload discovery is unavailable: {ex.Message}", ex, isUserError: true);
        }

        if (stackOnly)
        {
            results = [.. results.Where(r => r.Stack is not null)];
        }

        results = SelectChannelRows(results, effectivePrerelease);

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
                source = r.Source.Source,
                stack = r.Stack is null ? null : new
                {
                    stackIdentifier = r.Stack.StackIdentifier,
                    workerRuntime = r.Stack.WorkerRuntime,
                    displayName = r.Stack.DisplayName,
                    description = r.Stack.Description,
                },
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

        WriteFooter(results.Count);
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
    /// Picks each channel's newest actual row, so a preview title or stack declaration
    /// never leaks onto a stable version of the same package.
    /// </summary>
    private static IReadOnlyList<CatalogSearchResult> SelectChannelRows(IReadOnlyList<CatalogSearchResult> results, bool includePrerelease)
        => [.. results.GroupBy(r => r.Source.Source, StringComparer.Ordinal)
            .SelectMany(source => source.GroupBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase))
            .SelectMany(package => package
                .OrderByDescending(r => r.LatestVersion)
                .DistinctBy(r => GetChannel(r.LatestVersion))
                .Select(r => includePrerelease ? r with { Channel = GetChannel(r.LatestVersion) } : r))];

    private static void WriteCard(WorkloadCardWriter card, CatalogSearchResult result)
    {
        card.WriteHeading(result.Stack?.DisplayName ?? (string.IsNullOrWhiteSpace(result.Title) ? result.PackageId : result.Title));
        card.WriteField("Version", result.LatestVersion.ToNormalizedString());
        if (!string.IsNullOrEmpty(result.Channel))
        {
            card.WriteField("Channel", result.Channel);
        }

        card.WriteField("Package ID", result.PackageId);
        if (result.Stack is { } stack)
        {
            card.WriteField("Stack ID", stack.StackIdentifier);
        }

        card.WriteAliases(result.Aliases);
        card.WriteDescription(result.Stack?.Description ?? result.Description);
    }

    private void WriteFooter(int count)
    {
        _interaction.WriteBlankLine();
        _interaction.WriteHint($"Showing {count} {(count == 1 ? "result" : "results")}.");
        _interaction.WriteHint("Run 'func workload install <package-id> --exact' to install a package by ID.");
        _interaction.WriteHint("When several versions or channels exist, pass --version (-v) to install a specific one.");
    }
}
