// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads.Catalog;

namespace Azure.Functions.Cli.Commands.Workload;

/// <summary>
/// <c>func workload search [&lt;query&gt;]</c>. Renders matching workload
/// packages from the configured catalog as a table.
/// </summary>
internal sealed class WorkloadSearchCommand : FuncCliCommand
{
    private const int DefaultTake = 20;
    private const int DescriptionMaxLength = 80;
    private const string Placeholder = "-";

    private readonly IInteractionService _interaction;
    private readonly IWorkloadCatalog _catalog;

    public Argument<string?> QueryArgument { get; } = new("query")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "Optional free-form query. Omit to list all workloads.",
    };

    public Option<string?> SourceOption { get; } = new("--source")
    {
        Description = "NuGet feed URI to search.",
    };

    public Option<bool> IncludePrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Include prerelease versions in the results. Default: enabled while workloads are in preview.",
        DefaultValueFactory = _ => true,
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Emit machine-readable JSON instead of a table.",
    };

    public WorkloadSearchCommand(IInteractionService interaction, IWorkloadCatalog catalog)
        : base("search", "Search the workload catalog.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        Arguments.Add(QueryArgument);
        Options.Add(SourceOption);
        Options.Add(IncludePrereleaseOption);
        Options.Add(JsonOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? query = parseResult.GetValue(QueryArgument);
        string? source = parseResult.GetValue(SourceOption);
        bool includePrerelease = parseResult.GetValue(IncludePrereleaseOption);
        bool json = parseResult.GetValue(JsonOption);

        if (includePrerelease && !json)
        {
            _interaction.WriteHint(WorkloadInstallCommand.PrereleasePreviewHint);
        }

        // TODO: surface --skip and --take for paging once we have a UX
        // story for repeated queries (see workload spec §4.1).
        var searchQuery = new CatalogSearchQuery
        {
            Filter = query,
            IncludePrerelease = includePrerelease,
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

        IEnumerable<string[]> rows = results.Select(r => new[]
        {
            r.PackageId,
            r.Aliases.Count == 0 ? Placeholder : string.Join(", ", r.Aliases),
            string.IsNullOrWhiteSpace(r.Title) ? Placeholder : r.Title!,
            TruncateDescription(r.Description),
            r.LatestVersion.ToNormalizedString(),
        });

        _interaction.WriteTable(["ID", "Aliases", "Display Name", "Description", "Latest"], rows);
        return 0;
    }

    private static string TruncateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return Placeholder;
        }

        string trimmed = description.Trim();
        return trimmed.Length <= DescriptionMaxLength
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, DescriptionMaxLength - 3), "...");
    }
}
