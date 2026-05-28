// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Default implementation of <see cref="IQuickstartProviderResolver"/>.
/// Centralises stack and language resolution so individual quickstart
/// subcommands stay thin.
/// </summary>
internal sealed class QuickstartProviderResolver(
    IInteractionService interaction,
    IWorkloadHintRenderer hintRenderer,
    IEnumerable<IQuickstartProvider> providers) : IQuickstartProviderResolver
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadHintRenderer _hintRenderer = hintRenderer ?? throw new ArgumentNullException(nameof(hintRenderer));
    private readonly IReadOnlyList<IQuickstartProvider> _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToList();

    public async Task<IQuickstartProvider?> SelectProviderAsync(string? requestedStack, string actionDescription, CancellationToken cancellationToken)
    {
        string[] installed = [.. _providers.Select(p => p.Stack)];

        if (_providers.Count == 0)
        {
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.NoWorkloadsInstalled, actionDescription, null, installed));
            return null;
        }

        if (!string.IsNullOrEmpty(requestedStack))
        {
            IQuickstartProvider? match = _providers.FirstOrDefault(p =>
                string.Equals(p.Stack, requestedStack, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.NoMatchingStack, actionDescription, requestedStack, installed));
            return null;
        }

        if (_providers.Count == 1)
        {
            IQuickstartProvider sole = _providers[0];
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.AutoSelectedSoleWorkload, actionDescription, sole.Stack, installed));
            return sole;
        }

        if (!_interaction.IsInteractive)
        {
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.AmbiguousStackChoice, actionDescription, null, installed));
            return null;
        }

        var displayToProvider = new Dictionary<string, IQuickstartProvider>(StringComparer.Ordinal);
        foreach (IQuickstartProvider p in _providers)
        {
            displayToProvider.TryAdd(p.DisplayName, p);
        }
        string picked = await _interaction.PromptForSelectionAsync(
            "Select a stack:",
            displayToProvider.Keys,
            cancellationToken);

        return displayToProvider.TryGetValue(picked, out IQuickstartProvider? chosen) ? chosen : null;
    }

    public string? ResolveLanguage(string? requestedLanguage, IQuickstartProvider provider, QuickstartManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(requestedLanguage))
        {
            return null;
        }

        string? manifestLanguage = provider.ResolveManifestLanguage(requestedLanguage.Trim());
        if (manifestLanguage is null)
        {
            IEnumerable<string> displayNames = GetAvailableManifestLanguages(provider, manifest)
                .Select(provider.GetDisplayLanguage);
            _interaction.WriteError(
                $"Language '{requestedLanguage}' is not supported by the '{provider.Stack}' stack. " +
                $"Supported values: {string.Join(", ", displayNames)}.");
            return null;
        }

        IReadOnlyList<string> available = GetAvailableManifestLanguages(provider, manifest);
        if (!available.Contains(manifestLanguage, StringComparer.OrdinalIgnoreCase))
        {
            _interaction.WriteError(
                $"No quickstart templates found for language '{provider.GetDisplayLanguage(manifestLanguage)}' " +
                $"in the '{provider.Stack}' stack.");
            return null;
        }

        return manifestLanguage;
    }

    public async Task<(string? Language, int? ErrorCode)> ResolveOrPromptLanguageAsync(
        string? requestedLanguage,
        IQuickstartProvider provider,
        QuickstartManifest manifest,
        CancellationToken cancellationToken)
    {
        string? resolved = ResolveLanguage(requestedLanguage, provider, manifest);
        if (resolved is null && requestedLanguage is not null)
        {
            return (null, 1);
        }

        if (resolved is not null)
        {
            return (resolved, null);
        }

        return await PromptForLanguageAsync(provider, manifest, cancellationToken);
    }

    public async Task<(string? Language, int? ErrorCode)> PromptForLanguageAsync(
        IQuickstartProvider provider,
        QuickstartManifest manifest,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> available = GetAvailableManifestLanguages(provider, manifest);

        if (available.Count == 0)
        {
            _interaction.WriteError(
                $"No quickstart templates found for the '{provider.Stack}' stack.");
            return (null, 1);
        }

        if (available.Count == 1)
        {
            return (available[0], null);
        }

        if (!_interaction.IsInteractive)
        {
            IEnumerable<string> displayNames = available.Select(provider.GetDisplayLanguage);
            _interaction.WriteError(
                $"The '{provider.Stack}' stack supports multiple languages. " +
                $"Re-run with --language <{string.Join("|", displayNames)}>.");
            return (null, 1);
        }

        var displayToManifest = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string lang in available)
        {
            string display = provider.GetDisplayLanguage(lang);
            displayToManifest.TryAdd(display, lang);
        }

        string picked = await _interaction.PromptForSelectionAsync(
            "Select a language:",
            displayToManifest.Keys,
            cancellationToken);

        return (displayToManifest[picked], null);
    }

    public IReadOnlyList<string> GetAvailableManifestLanguages(IQuickstartProvider provider, QuickstartManifest manifest)
    {
        HashSet<string> manifestLanguages = new(
            manifest.Entries.Select(e => e.Language),
            StringComparer.OrdinalIgnoreCase);

        return [.. provider.ManifestLanguages
            .Where(manifestLanguages.Contains)];
    }

    public IQuickstartProvider? FindProviderForLanguage(string manifestLanguage)
    {
        return _providers.FirstOrDefault(p =>
            p.ManifestLanguages.Contains(manifestLanguage, StringComparer.OrdinalIgnoreCase));
    }
}
