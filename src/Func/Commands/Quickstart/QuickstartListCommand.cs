// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// <c>func quickstart list [--language] [--resource] [--iac] [--search]</c>.
/// Lists available function app templates from the CDN manifest.
/// </summary>
internal sealed class QuickstartListCommand : FuncCliCommand
{
    private readonly IInteractionService _interaction;
    private readonly IQuickstartManifestClient _manifestClient;

    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "Filter by programming language (e.g. CSharp, Python, TypeScript).",
    };

    public Option<string?> ResourceOption { get; } = new("--resource", "-r")
    {
        Description = "Filter by Azure resource type (e.g. 'HTTP Trigger', 'Timer Trigger').",
    };

    public Option<string?> IacOption { get; } = new("--iac")
    {
        Description = "Filter by infrastructure-as-code type (e.g. Bicep, Terraform).",
    };

    public Option<string?> SearchOption { get; } = new("--search", "-s")
    {
        Description = "Search template IDs, display names, resources, descriptions, and tags.",
    };

    public QuickstartListCommand(
        IInteractionService interaction,
        IQuickstartManifestClient manifestClient)
        : base("list", "List available function app templates.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));

        Options.Add(LanguageOption);
        Options.Add(ResourceOption);
        Options.Add(IacOption);
        Options.Add(SearchOption);
    }

    protected override async Task<int> ExecuteAsync(
        ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? language = parseResult.GetValue(LanguageOption);
        string? resource = parseResult.GetValue(ResourceOption);
        string? iac      = parseResult.GetValue(IacOption);
        string? search   = parseResult.GetValue(SearchOption);

        QuickstartManifest manifest;
        try
        {
            manifest = await _interaction.ShowStatusAsync(
                "Fetching quickstart manifest…",
                _manifestClient.GetManifestAsync,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new GracefulException(ex.Message, isUserError: true);
        }

        IReadOnlyList<QuickstartEntry> templates = manifest.Filter(language, resource, iac, search);

        if (templates.Count == 0)
        {
            string message = !string.IsNullOrWhiteSpace(search)
                ? $"No templates found matching '{search}'. Run 'func quickstart list' to browse all available templates."
                : "No templates matched the specified filters. Run 'func quickstart list' to browse all available templates.";
            _interaction.WriteHint(message);
            return 1;
        }

        // Omit a column when its filter was already applied (it would be repetitive)
        // or, for IaC, when no results carry an IaC value.
        bool showLanguage = string.IsNullOrWhiteSpace(language);
        bool showResource = string.IsNullOrWhiteSpace(resource);
        bool showIac      = templates.Any(t => !string.IsNullOrEmpty(t.Iac));

        List<string> headers = ["ID", "Display Name"];
        if (showLanguage) headers.Add("Language");
        if (showResource) headers.Add("Resource");
        if (showIac)      headers.Add("IaC");

        IEnumerable<string[]> rows = templates.Select(t =>
        {
            List<string> row = [t.Id, t.DisplayName];
            if (showLanguage) row.Add(t.Language);
            if (showResource) row.Add(t.Resource);
            if (showIac)      row.Add(t.Iac ?? "-");
            return row.ToArray();
        });

        _interaction.WriteTable([.. headers], rows);
        return 0;
    }
}
