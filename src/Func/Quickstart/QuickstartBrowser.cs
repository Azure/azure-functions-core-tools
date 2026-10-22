// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Quickstart;

internal sealed record QuickstartBrowseRequest(
    string? Stack = null, string? Language = null, string? Resource = null, string? Iac = null, string? Search = null);

/// <summary>
/// Presents discovery metadata only. Completion never returns an acquisition or creation selection.
/// </summary>
internal interface IQuickstartBrowser
{
    public Task BrowseAsync(QuickstartBrowseRequest request, CancellationToken cancellationToken);
}

internal sealed class QuickstartBrowser(
    IQuickstartCatalogService catalog,
    InstalledProjectStackCatalog stacks,
    IEnumerable<IQuickstartProvider> providers,
    IInteractionService interaction,
    QuickstartCatalogRenderer renderer) : IQuickstartBrowser
{
    private const string Back = "Back";
    private const string Cancel = "Cancel browsing";
    private const string Search = "Change search";
    private const string Resource = "Change resource";
    private const string Iac = "Change IaC";
    private const string Clear = "Clear filters";

    private readonly IQuickstartCatalogService _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    private readonly InstalledProjectStackCatalog _stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
    private readonly IReadOnlyList<IQuickstartProvider> _providers = [.. providers ?? throw new ArgumentNullException(nameof(providers))];
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly QuickstartCatalogRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

    public async Task BrowseAsync(QuickstartBrowseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        IProjectStack? stack = await SelectStackAsync(request.Stack, cancellationToken);
        if (stack is null)
        {
            return;
        }

        IQuickstartProvider? provider = _providers.SingleOrDefault(candidate =>
            string.Equals(candidate.Stack, stack.Stack, StringComparison.OrdinalIgnoreCase));
        InstalledProjectStackCatalog selected = new([stack]);
        string? language = null;
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            language = selected.GetCanonicalLanguage(request.Language);
            if (language is null)
            {
                _interaction.WriteError($"Language '{request.Language}' is not supported by '{stack.Stack}'. Choose one of: {string.Join(", ", stack.SupportedLanguages)}.");
                return;
            }
        }

        QuickstartCatalogQuery query = new(Resource: request.Resource, Iac: request.Iac, Search: request.Search,
            LegacyStack: stack, LegacyLanguageProvider: provider);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QuickstartCatalog results = await _interaction.ShowStatusAsync(
                "Loading quickstart discovery metadata...",
                ct => _catalog.SearchAsync(query, ct),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (language is not null)
            {
                results = results with
                {
                    Entries = results.Entries.Where(item => string.Equals(language,
                        selected.GetCanonicalLanguage(provider?.GetDisplayLanguage(item.Entry.Language) ?? item.Entry.Language),
                        StringComparison.OrdinalIgnoreCase)).ToList().AsReadOnly(),
                };
            }

            _renderer.WriteProvenance(results.Provenance);
            _interaction.WriteHint($"Discovery stack filter: {stack.DisplayName}. This does not constrain generated project topology.");
            _interaction.WriteLine(line => line.Muted(
                $"Language: {language ?? "all"}; search: {query.Search ?? "all"}; resource: {query.Resource ?? "all"}; IaC: {query.Iac ?? "all"}."));
            _renderer.WriteResults(results.Entries);
            if (!_interaction.IsInteractive)
            {
                return;
            }

            // Ordinals disambiguate even duplicate catalog IDs without treating either as an owner.
            Dictionary<string, QuickstartCatalogEntry> choices = [];
            for (int i = 0; i < results.Entries.Count; i++)
            {
                QuickstartCatalogEntry item = results.Entries[i];
                choices.Add($"View {i + 1}. {item.Entry.DisplayName} ({item.Entry.Id}) [{item.MetadataStatus}]", item);
            }

            string action = await _interaction.PromptForSelectionAsync(
                "Browse templates (details only):",
                choices.Keys.Concat([Search, Resource, Iac, Clear, Back, Cancel]),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (choices.TryGetValue(action, out QuickstartCatalogEntry? entry))
            {
                _renderer.WriteDetails(entry, provider?.GetDisplayLanguage(entry.Entry.Language) ?? entry.Entry.Language);
                string navigation = await _interaction.PromptForSelectionAsync(
                    "Template details (no install or create action):", [Back, Cancel], cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (navigation != Back)
                {
                    return;
                }
            }
            else if (action is Search or Resource or Iac)
            {
                string value = await _interaction.PromptForInputAsync(
                    $"{action} (empty clears this filter):", cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                string? filter = string.IsNullOrWhiteSpace(value) ? null : value;
                query = action switch
                {
                    Search => query with { Search = filter },
                    Resource => query with { Resource = filter },
                    _ => query with { Iac = filter },
                };
            }
            else if (action == Clear)
            {
                query = query with { Resource = null, Iac = null, Search = null };
            }
            else
            {
                return;
            }
        }
    }

    private async Task<IProjectStack?> SelectStackAsync(string? requested, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            IProjectStack? found = _stacks.FindStack(requested);
            if (found is null)
            {
                _interaction.WriteError($"Stack '{requested}' is not installed. Run 'func setup' to configure a stack.");
            }

            return found;
        }

        if (_stacks.Stacks.Count == 1)
        {
            return _stacks.Stacks[0];
        }

        if (_stacks.Stacks.Count == 0)
        {
            _interaction.WriteError("No installed stack capabilities are available. Run 'func setup' before browsing by stack.");
            return null;
        }

        if (!_interaction.IsInteractive)
        {
            _interaction.WriteError("Choose an installed stack explicitly when browsing without interactive input.");
            return null;
        }

        var choices = _stacks.Stacks.ToDictionary(
            stack => $"{stack.DisplayName} ({stack.Stack})", StringComparer.Ordinal);
        string picked = await _interaction.PromptForSelectionAsync(
            "Select a discovery stack filter:", choices.Keys.Append(Cancel), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return choices.GetValueOrDefault(picked);
    }
}
