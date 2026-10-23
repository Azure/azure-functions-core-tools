// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Quickstart;

internal sealed record QuickstartBrowseRequest(
    string? Stack = null, string? Language = null, string? Resource = null, string? Iac = null, string? Search = null)
{
    internal QuickstartInstalledQuery? InstalledQuery { get; init; }

    internal bool AllowSelection { get; init; }
}

internal enum QuickstartBrowseOutcome
{
    Canceled,
    Back,
    Selected,
}

internal sealed record QuickstartBrowseResult(QuickstartBrowseOutcome Outcome, QuickstartCatalogEntry? Entry = null);

/// <summary>
/// Presents discovery metadata. An explicit selection nominates metadata, never acquisition authority.
/// </summary>
internal interface IQuickstartBrowser
{
    public Task<QuickstartBrowseResult> BrowseAsync(QuickstartBrowseRequest request, CancellationToken cancellationToken);
}

internal sealed class QuickstartBrowser(
    IQuickstartCatalogService catalog,
    InstalledProjectStackCatalog stacks,
    IEnumerable<IQuickstartProvider> providers,
    IInteractionService interaction,
    QuickstartCatalogRenderer renderer,
    QuickstartInstalledTemplates? installed = null) : IQuickstartBrowser
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
    private readonly QuickstartInstalledTemplates? _installed = installed;

    public async Task<QuickstartBrowseResult> BrowseAsync(QuickstartBrowseRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        IProjectStack? stack = await SelectStackAsync(request.Stack, cancellationToken);
        if (stack is null)
        {
            return new(QuickstartBrowseOutcome.Canceled);
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
                return new(QuickstartBrowseOutcome.Canceled);
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
            QuickstartActionableEntry[]? actionable = null;
            if (request.AllowSelection)
            {
                if (_installed is null || request.InstalledQuery is not { } installedQuery)
                {
                    _interaction.WriteError("The CLI cannot verify available templates for this selection. No unverified discovery entry can be selected.");
                    return new(QuickstartBrowseOutcome.Canceled);
                }

                QuickstartInstalledProjection projection = await _installed.ProjectAsync(results,
                    installedQuery with { Stack = stack.Stack, Language = language }, cancellationToken);
                actionable = [.. projection.ActionableEntries.Where(item => item.Availability != QuickstartAvailability.Installed)];
                results = results with { Entries = actionable.Select(item => item.Entry).ToList().AsReadOnly() };
                if (projection.ExcludedCount > 0)
                {
                    _interaction.WriteHint($"{projection.ExcludedCount} discovery entries were excluded because they are not currently qualified for this CLI. Run 'func quickstart list' for catalog diagnostics.");
                }
            }

            _renderer.WriteProvenance(results.Provenance);
            _interaction.WriteHint($"Discovery stack filter: {stack.DisplayName}. This does not constrain generated project topology.");
            _interaction.WriteLine(line => line.Muted(
                $"Language: {language ?? "all"}; search: {query.Search ?? "all"}; resource: {query.Resource ?? "all"}; IaC: {query.Iac ?? "all"}."));
            if (actionable is null)
            {
                _renderer.WriteResults(results.Entries);
            }
            else
            {
                _renderer.WriteActionableResults(actionable);
            }
            if (!_interaction.IsInteractive)
            {
                return new(QuickstartBrowseOutcome.Canceled);
            }

            Dictionary<string, QuickstartCatalogEntry> diagnosticChoices = actionable is null
                ? CreateDiagnosticChoices(results.Entries)
                : [];
            Dictionary<string, QuickstartActionableEntry> actionableChoices = actionable is null
                ? []
                : CreateActionableChoices(actionable);
            IEnumerable<string> choices = actionable is null ? diagnosticChoices.Keys : actionableChoices.Keys;

            string action = await _interaction.PromptForSelectionAsync(
                request.AllowSelection ? "Select a qualified template:" : "Browse templates (details only):",
                choices.Concat([Search, Resource, Iac, Clear, Back, Cancel]),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            QuickstartCatalogEntry? entry = diagnosticChoices.GetValueOrDefault(action)
                ?? actionableChoices.GetValueOrDefault(action)?.Entry;
            if (entry is not null)
            {
                _renderer.WriteDetails(entry, provider?.GetDisplayLanguage(entry.Entry.Language) ?? entry.Entry.Language);
                string navigation = await _interaction.PromptForSelectionAsync(
                    request.AllowSelection ? "Template details (selection still requires qualification and consent):" : "Template details (no install or create action):",
                    request.AllowSelection ? ["Use this template", Back, Cancel] : [Back, Cancel], cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (request.AllowSelection && navigation == "Use this template")
                {
                    return new(QuickstartBrowseOutcome.Selected, entry);
                }
                if (navigation != Back)
                {
                    return new(QuickstartBrowseOutcome.Canceled);
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
                return new(action == Back ? QuickstartBrowseOutcome.Back : QuickstartBrowseOutcome.Canceled);
            }
        }
    }

    private static Dictionary<string, QuickstartCatalogEntry> CreateDiagnosticChoices(IReadOnlyList<QuickstartCatalogEntry> entries)
    {
        Dictionary<string, QuickstartCatalogEntry> choices = new(StringComparer.Ordinal);
        foreach (QuickstartCatalogEntry entry in entries)
        {
            string label = $"{entry.Entry.DisplayName} ({entry.Entry.Id})";
            AddUniqueChoice(choices, label, entry, entry.Entry.Language, entry.Entry.Resource, entry.Entry.Iac,
                entry.PackageReference?.PackageId, entry.PackageReference?.Version, entry.Entry.ShortDescription);
        }
        return choices;
    }

    private static Dictionary<string, QuickstartActionableEntry> CreateActionableChoices(IReadOnlyList<QuickstartActionableEntry> entries)
    {
        Dictionary<string, QuickstartActionableEntry> choices = new(StringComparer.Ordinal);
        foreach (QuickstartActionableEntry candidate in entries)
        {
            QuickstartEntry entry = candidate.Entry.Entry;
            string label = $"{entry.DisplayName} ({entry.Id}) — {AvailabilityLabel(candidate.Availability)}";
            AddUniqueChoice(choices, label, candidate, entry.Language, entry.Resource, entry.Iac,
                candidate.Entry.PackageReference?.PackageId, candidate.Entry.PackageReference?.Version,
                $"{candidate.Stack}; {entry.ShortDescription}");
        }
        return choices;
    }

    private static void AddUniqueChoice<T>(Dictionary<string, T> choices, string label, T value,
        string? language, string resource, string? iac, string? packageId, string? version, string? detail)
    {
        string unique = label;
        if (choices.ContainsKey(unique))
        {
            unique += $" — {language ?? "unknown language"}, {resource}, IaC {iac ?? "none"}";
        }
        if (choices.ContainsKey(unique))
        {
            unique += $" — {packageId ?? "unknown package"} {version ?? "unknown version"}";
        }
        if (choices.ContainsKey(unique))
        {
            unique += $" — {detail ?? "duplicate catalog entry"}";
        }
        for (int duplicate = 2; choices.ContainsKey(unique); duplicate++)
        {
            unique = $"{label} — duplicate match {duplicate}";
        }
        choices.Add(unique, value);
    }

    private static string AvailabilityLabel(QuickstartAvailability availability) => availability switch
    {
        QuickstartAvailability.AvailableToInstall => "Available to install",
        QuickstartAvailability.Installed => "Installed",
        QuickstartAvailability.UpdateAvailable => "Update available",
        _ => throw new ArgumentOutOfRangeException(nameof(availability)),
    };

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
