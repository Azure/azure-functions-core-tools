// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Commands.Quickstart;

/// <summary>
/// Lists available templates from the CDN manifest, filtered by the resolved
/// stack and language. Delegates stack/language resolution to
/// <see cref="IQuickstartProviderResolver"/>.
/// </summary>
internal sealed class QuickstartListCommand : FuncCliCommand
{
    public Option<string?> StackOption { get; } = new("--stack", "-s")
    {
        Description = QuickstartMessages.StackOptionDescription
    };

    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "The programming language"
    };

    public Option<string?> ResourceOption { get; } = new("--resource", "-r")
    {
        Description = "Filter by trigger/binding resource (e.g. http, timer, blob, eventhub, servicebus, cosmos, sql, mcp, durable)"
    };

    public Option<string?> IacOption { get; } = new("--iac")
    {
        Description = "Filter by infrastructure-as-code type (e.g. bicep, terraform, none)"
    };

    public Option<string?> SearchOption { get; } = new("--search")
    {
        Description = "Case-insensitive substring match against IDs, template names, resource type, Infrastructure as Code type, and descriptions"
    };

    public Option<bool> JsonOption { get; } = new("--json")
    {
        Description = "Emit machine-readable JSON instead of a table."
    };

    private readonly IInteractionService _interaction;
    private readonly IQuickstartProviderResolver _resolver;
    private readonly IQuickstartManifestService _manifestService;

    public QuickstartListCommand(
        IInteractionService interaction,
        IQuickstartProviderResolver resolver,
        IQuickstartManifestService manifestService)
        : base("list", "List available templates from the catalog.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(manifestService);

        _interaction = interaction;
        _resolver = resolver;
        _manifestService = manifestService;

        Options.Add(StackOption);
        Options.Add(LanguageOption);
        Options.Add(ResourceOption);
        Options.Add(IacOption);
        Options.Add(SearchOption);
        Options.Add(JsonOption);
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        string? requestedStack = parseResult.GetValue(StackOption);
        string? requestedLanguage = parseResult.GetValue(LanguageOption);
        string? resource = parseResult.GetValue(ResourceOption);
        string? iac = parseResult.GetValue(IacOption);
        string? search = parseResult.GetValue(SearchOption);
        bool json = parseResult.GetValue(JsonOption);

        // 1. Resolve stack → provider
        IQuickstartProvider? provider = await _resolver.SelectProviderAsync(requestedStack, "list quickstart templates", cancellationToken);
        if (provider is null)
        {
            return 1;
        }

        // 2. Fetch manifest
        QuickstartManifest manifest = await _interaction.ShowStatusAsync(
            QuickstartMessages.FetchingCatalogStatus,
            _manifestService.GetManifestAsync,
            cancellationToken);

        // 3. Resolve language
        (string? manifestLanguage, int? langError) = await _resolver.ResolveOrPromptLanguageAsync(
            requestedLanguage, provider, manifest, cancellationToken);
        if (langError is int code)
        {
            return code;
        }

        // 4. Filter and display
        IReadOnlyList<QuickstartEntry> entries = manifest.Filter(manifestLanguage, resource, iac, search);

        if (entries.Count == 0)
        {
            _interaction.WriteWarning(QuickstartMessages.NoMatchingFiltersWarning);
            return 0;
        }

        if (json)
        {
            _interaction.WriteJson(entries.Select(e => new ListRow(e.Id, e.DisplayName, e.ShortDescription ?? string.Empty)).ToList());
            return 0;
        }

        string[] columns = ["ID", "Name", "Description"];
        IEnumerable<string[]> rows = entries.Select(e => new[]
        {
            e.Id,
            e.DisplayName,
            e.ShortDescription ?? "-"
        });

        _interaction.WriteTable(columns, rows);
        _interaction.WriteLine(l => l.Muted($"{entries.Count} template(s) found."));
        return 0;
    }

    internal sealed record ListRow(string Id, string Name, string Description);
}
