// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Packages;
using Azure.Functions.Cli.Workloads.Catalog;

namespace Azure.Functions.Cli.Commands.Templates;

/// <summary>
/// Searches template packages without expanding aliases or version channels.
/// </summary>
internal sealed class TemplatesSearchCommand : FuncCliCommand
{
    private readonly TemplateCatalog _catalog;
    private readonly IInteractionService _interaction;

    public Argument<string?> QueryArgument { get; } = new("query")
    {
        Arity = ArgumentArity.ZeroOrOne,
        Description = "Optional free-text package query.",
    };

    public Option<string?> SourceOption { get; } = new("--source") { Description = "Additional HTTP(S) V3 NuGet feed to search directly (defaults to nuget.org)." };

    public Option<bool?> PrereleaseOption { get; } = new("--prerelease")
    {
        Description = "Include prerelease packages (defaults to the CLI's release status).",
    };

    public Option<bool> JsonOption { get; } = new("--json") { Description = "Emit machine-readable package JSON instead of cards." };

    public TemplatesSearchCommand(TemplateCatalog catalog, IInteractionService interaction)
        : base("search", "Search for FuncTemplate packages.")
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        Arguments.Add(QueryArgument);
        Options.Add(SourceOption);
        Options.Add(PrereleaseOption);
        Options.Add(JsonOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        IReadOnlyList<CatalogSearchResult> results;
        try
        {
            results = await _catalog.SearchAsync(parseResult.GetValue(QueryArgument), parseResult.GetValue(SourceOption),
                parseResult.GetValue(PrereleaseOption), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }
        catch (TemplatePackageException ex)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }
        if (parseResult.GetValue(JsonOption))
        {
            _interaction.WriteJson(results.Select(r => new TemplatePackageSearchResult(
                r.PackageId, r.LatestVersion.ToNormalizedString(), r.Title, r.Description)));
            return 0;
        }

        if (results.Count == 0)
        {
            _interaction.WriteWarning("No template packages found.");
            return 0;
        }

        foreach (CatalogSearchResult result in results)
        {
            _interaction.WriteLine(line => line.Heading(string.IsNullOrWhiteSpace(result.Title) ? result.PackageId : result.Title));
            _interaction.WriteLine(line => line.Command("Package ID: ").Muted(result.PackageId));
            _interaction.WriteLine(line => line.Command("Version: ").Muted(result.LatestVersion.ToNormalizedString()));
            _interaction.WriteLine(line => line.Command("Description: ").Muted(result.Description ?? "(no description)"));
            _interaction.WriteBlankLine();
        }

        _interaction.WriteHint("Run 'func templates install <package-id>' to install a package (stable by default).");
        if (results.Count == 20)
        {
            _interaction.WriteHint("More packages may be available; refine your query.");
        }

        return 0;
    }
}
