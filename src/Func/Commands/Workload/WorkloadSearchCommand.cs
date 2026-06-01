// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Catalog;
using Microsoft.Extensions.Options;

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

        if (json)
        {
            _interaction.WriteJson(results.Select(r => new
            {
                packageId = r.PackageId,
                latestVersion = r.LatestVersion.ToNormalizedString(),
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

        WriteFooter(results.Count);
        return 0;
    }

    private static void WriteCard(WorkloadCardWriter card, CatalogSearchResult result)
    {
        card.WriteHeading(DisplayNameOrPackageId(result));
        card.WriteField("Version", result.LatestVersion.ToNormalizedString());
        card.WriteField("Package ID", result.PackageId);
        card.WriteAliases(result.Aliases);
        card.WriteDescription(result.Description);
    }

    private void WriteFooter(int count)
    {
        _interaction.WriteBlankLine();
        string countLine = $"Showing {count} {(count == 1 ? "result" : "results")}.";
        if (count >= DefaultTake)
        {
            countLine += " More may be available, refine your query.";
        }

        _interaction.WriteHint(countLine);
        _interaction.WriteHint("Run 'func workload install <alias>' to install one.");
    }

    private static string DisplayNameOrPackageId(CatalogSearchResult result)
        => string.IsNullOrWhiteSpace(result.Title) ? result.PackageId : result.Title!;
}
