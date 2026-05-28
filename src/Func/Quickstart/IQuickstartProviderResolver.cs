// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Encapsulates the stack → provider and language resolution flow shared by
/// all <c>func quickstart</c> subcommands. Commands pass an action description
/// so hint messages stay contextual.
/// </summary>
internal interface IQuickstartProviderResolver
{
    /// <summary>
    /// Resolves a stack option to a single <see cref="IQuickstartProvider"/>.
    /// When the stack is unambiguous (single workload, explicit match), returns
    /// it directly. Otherwise prompts interactively or renders a workload hint
    /// and returns <c>null</c>.
    /// </summary>
    public Task<IQuickstartProvider?> SelectProviderAsync(string? requestedStack, string actionDescription, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves user language input (e.g. "c#", "js") to a manifest language
    /// value via the provider's mapping. Returns <c>null</c> when the input is
    /// not recognized or has no templates.
    /// </summary>
    public string? ResolveLanguage(string? requestedLanguage, IQuickstartProvider provider, QuickstartManifest manifest);

    /// <summary>
    /// Prompts interactively for a language when none was supplied on the
    /// command line and the provider supports multiple languages. Auto-selects
    /// when only one language has templates.
    /// </summary>
    public Task<(string? Language, int? ErrorCode)> PromptForLanguageAsync(IQuickstartProvider provider, QuickstartManifest manifest, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the language from user input or, when absent, prompts
    /// interactively. Combines <see cref="ResolveLanguage"/> and
    /// <see cref="PromptForLanguageAsync"/> into a single call so commands
    /// don't duplicate the resolve-then-prompt flow.
    /// </summary>
    public Task<(string? Language, int? ErrorCode)> ResolveOrPromptLanguageAsync(
        string? requestedLanguage, IQuickstartProvider provider, QuickstartManifest manifest, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the manifest language values that the provider handles and that
    /// have at least one entry in the manifest.
    /// </summary>
    public IReadOnlyList<string> GetAvailableManifestLanguages(IQuickstartProvider provider, QuickstartManifest manifest);

    /// <summary>
    /// Finds the provider that owns the given manifest language value, or
    /// <c>null</c> if no installed provider claims it.
    /// </summary>
    public IQuickstartProvider? FindProviderForLanguage(string manifestLanguage);
}
