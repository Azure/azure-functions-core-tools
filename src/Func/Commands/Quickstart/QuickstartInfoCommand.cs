// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// Displays detailed information about a specific template by id.
/// Resolves the owning provider from the entry's language for display-name
/// mapping.
/// </summary>
internal sealed class QuickstartInfoCommand : FuncCliCommand
{
    public Argument<string> TemplateIdArgument { get; } = new("id")
    {
        Description = "Template ID from the manifest (e.g. http-trigger-python-azd). Use 'func quickstart list' to see available IDs."
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Emit machine-readable JSON instead of formatted output."
    };

    private readonly IInteractionService _interaction;
    private readonly IQuickstartProviderResolver _resolver;
    private readonly IQuickstartCatalogService _catalogService;
    private readonly QuickstartCatalogRenderer _renderer;

    public QuickstartInfoCommand(
        IInteractionService interaction,
        IQuickstartProviderResolver resolver,
        IQuickstartCatalogService catalogService,
        QuickstartCatalogRenderer renderer)
        : base("info", "Show detailed information about a template.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(catalogService);
        ArgumentNullException.ThrowIfNull(renderer);

        _interaction = interaction;
        _resolver = resolver;
        _catalogService = catalogService;
        _renderer = renderer;

        Arguments.Add(TemplateIdArgument);
        Options.Add(JsonOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string templateId = parseResult.GetValue(TemplateIdArgument)!;
        bool json = parseResult.GetValue(JsonOption);

        QuickstartCatalog catalog = await _interaction.ShowStatusAsync(
            QuickstartMessages.FetchingCatalogStatus,
            ct => _catalogService.SearchAsync(new(), ct),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        QuickstartCatalogEntry? item = catalog.Entries.FirstOrDefault(e =>
            string.Equals(e.Entry.Id, templateId, StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            _interaction.WriteError(
                $"Template '{templateId}' not found. " + QuickstartMessages.TemplateNotFoundHint);
            return 1;
        }

        QuickstartEntry entry = item.Entry;
        IQuickstartProvider? provider = _resolver.FindProviderForLanguage(entry.Language);
        string displayLanguage = provider?.GetDisplayLanguage(entry.Language) ?? entry.Language;

        if (json)
        {
            _interaction.WriteJson(new InfoRow(
                entry.Id,
                entry.DisplayName,
                displayLanguage,
                entry.Resource,
                entry.Iac ?? "none",
                entry.GitRef,
                entry.RepositoryUrl,
                entry.FolderPath,
                entry.ShortDescription,
                entry.LongDescription,
                entry.WhatsIncluded));
            return 0;
        }

        _renderer.WriteProvenance(catalog.Provenance);
        _renderer.WriteDetails(item, displayLanguage);

        _interaction.WriteBlankLine();
        string stack = provider?.Stack ?? entry.Language.ToLowerInvariant();
        if (item.MetadataStatus == QuickstartPackageMetadataStatus.Unverified && item.PackageReference?.Artifact is not null)
        {
            string stackOption = provider is null ? string.Empty : $" --stack {provider.Stack}";
            _interaction.WriteHint($"Inspect without acquisition: func init <path> --guided{stackOption} --quickstart {entry.Id} --dry-run");
            _interaction.WriteHint("Package metadata is unverified. Creation requires an installed stack and independent artifact qualification; this listing does not establish acquisition readiness.");
        }
        else
        {
            _interaction.WriteHint("This entry has no usable package acquisition metadata. The legacy source scaffolder remains available, separate from the func init template experience.");
            _interaction.WriteLine(l => l.Muted($"  Legacy source scaffolder: func quickstart <path> --stack {stack} --language {displayLanguage.ToLowerInvariant()} --template {entry.Id}"));
        }

        return 0;
    }

    internal sealed record InfoRow(
        string Id,
        string Name,
        string Language,
        string Resource,
        string Iac,
        string? GitRef,
        string RepositoryUrl,
        string FolderPath,
        string? ShortDescription,
        string? LongDescription,
        IReadOnlyList<string>? WhatsIncluded);
}
