// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Quickstart;

internal sealed class QuickstartProviderResolver(IInteractionService interaction, IWorkloadHintRenderer hintRenderer, IEnumerable<IQuickstartProvider> providers, InstalledProjectStackCatalog catalog) : IQuickstartProviderResolver
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadHintRenderer _hintRenderer = hintRenderer ?? throw new ArgumentNullException(nameof(hintRenderer));
    private readonly IReadOnlyList<IQuickstartProvider> _providers = [.. providers ?? throw new ArgumentNullException(nameof(providers))];
    private readonly InstalledProjectStackCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public async Task<IQuickstartProvider?> SelectProviderAsync(string? requestedStack, string actionDescription, CancellationToken cancellationToken)
    {
        string[] installed = [.. _providers.Select(p => p.Stack.Descriptor.Identifier)];
        IQuickstartProvider[] candidates = requestedStack is null ? [.. _providers] : [.. _providers.Where(p =>
            string.Equals(p.Stack.Descriptor.Identifier, requestedStack.Trim(), StringComparison.OrdinalIgnoreCase))];

        if (candidates.Length == 0)
        {
            WorkloadHintKind kind = _providers.Count == 0 ? WorkloadHintKind.NoWorkloadsInstalled : WorkloadHintKind.NoMatchingStack;
            _hintRenderer.Render(new WorkloadHint(kind, actionDescription, requestedStack, installed));
            return null;
        }

        string? conflict = candidates.Select(GetProviderConflict).FirstOrDefault(message => message is not null);
        if (conflict is not null)
        {
            _interaction.WriteError(conflict);
            return null;
        }

        if (candidates.Length == 1)
        {
            if (requestedStack is null)
            {
                _hintRenderer.Render(new WorkloadHint(WorkloadHintKind.AutoSelectedSoleWorkload, actionDescription, candidates[0].Stack.Descriptor.Identifier, installed));
            }

            return candidates[0];
        }

        if (!_interaction.IsInteractive)
        {
            _hintRenderer.Render(new WorkloadHint(WorkloadHintKind.AmbiguousStackChoice, actionDescription, null, installed));
            return null;
        }

        return await _interaction.PromptForSelectionAsync("Select a stack:", candidates, p => $"{p.Stack.Descriptor.DisplayName} ({p.Stack.Descriptor.Identifier})", cancellationToken);
    }

    public async Task<LanguageDescriptor?> ResolveOrPromptLanguageAsync(string? requestedLanguage, IQuickstartProvider provider, QuickstartManifest manifest, CancellationToken cancellationToken)
    {
        IProjectStack stack = provider.Stack;
        string id = stack.Descriptor.Identifier;
        LanguageDescriptor[] available = [.. stack.SupportedLanguages.Where(l => manifest.Entries.Any(e => l.IsMatch(e.Language)))];
        if (requestedLanguage is not null)
        {
            LanguageDescriptor language;
            try
            {
                language = InstalledProjectStackCatalog.ResolveLanguage(stack, requestedLanguage, "--language");
            }
            catch (ProjectStackResolutionException ex)
            {
                _interaction.WriteError(ex.Message);
                return null;
            }

            if (available.Contains(language))
            {
                return language;
            }

            _interaction.WriteError($"No quickstart templates found for language '{language.DisplayName}' in the '{id}' stack.");
            return null;
        }

        if (available.Length == 0)
        {
            _interaction.WriteError($"No quickstart templates found for the '{id}' stack.");
            return null;
        }

        if (available.Length == 1)
        {
            return available[0];
        }

        if (!_interaction.IsInteractive)
        {
            _interaction.WriteError($"The '{id}' stack supports multiple languages. Re-run with --language <{string.Join("|", available.Select(l => l.LanguageIdentifier))}>.");
            return null;
        }

        return await _interaction.PromptForSelectionAsync("Select a language:", available, l => $"{l.DisplayName} ({l.LanguageIdentifier})", cancellationToken);
    }

    public (IQuickstartProvider Provider, LanguageDescriptor Language)? FindUniqueProviderForLanguage(string manifestLanguage)
    {
        (IQuickstartProvider Provider, LanguageDescriptor Language)[] matches = [.. _providers
            .SelectMany(p => p.Stack.SupportedLanguages.Where(l => l.IsMatch(manifestLanguage)).Select(l => (p, l))).Take(2)];
        if (matches.Length != 1)
        {
            return null;
        }

        return GetProviderConflict(matches[0].Provider) is null ? matches[0] : null;
    }

    private string? GetProviderConflict(IQuickstartProvider provider)
    {
        StackDescriptor stack = provider.Stack.Descriptor;
        IProjectStack? owner;
        try
        {
            owner = _catalog.FindStack(stack.Identifier);
        }
        catch (ProjectStackResolutionException ex)
        {
            return ex.Message;
        }

        return ReferenceEquals(owner, provider.Stack) && _providers.Count(p => p.Stack.Descriptor == stack) == 1
            ? null : $"Stack '{stack.Identifier}' does not have a unique installed quickstart provider.";
    }
}
