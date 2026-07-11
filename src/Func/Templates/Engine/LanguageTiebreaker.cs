// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Narrows a <see cref="TemplateGroup"/> to a single language variant when
/// <see cref="TemplateGroupResolver.Resolve"/> alone cannot (a group that carries
/// more than one language, e.g. the Node JavaScript/TypeScript pair). The tiebreak
/// consults, in order:
/// <list type="number">
/// <item>the <em>ambient</em> language inferred from the resolved project, then</item>
/// <item>the explicit <c>--language</c> value, then</item>
/// <item>an interactive prompt — but only when a genuine choice still remains.</item>
/// </list>
/// <para>
/// Because a single <c>--language</c> flag drives both stack and variant selection
/// (see the stack-resolution order), the ambient signal is the deciding factor
/// precisely when <c>--language</c> is absent; when it is supplied it decides the
/// variant directly. An explicit language that matches no variant surfaces the
/// resolver's <see cref="TemplateVariantResolution.NoMatch"/> rather than falling
/// through to a prompt, so an unsatisfiable request fails instead of silently
/// asking. Cross-stack override semantics and the accompanying advisory are layered
/// on separately.
/// </para>
/// </summary>
internal sealed class LanguageTiebreaker
{
    private readonly TemplateGroupResolver _resolver;
    private readonly IInteractionService _interaction;

    public LanguageTiebreaker(TemplateGroupResolver resolver, IInteractionService interaction)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    }

    /// <summary>
    /// Resolves <paramref name="group"/> to a winning variant, applying the
    /// ambient → <c>--language</c> → prompt tiebreak. <paramref name="ambientLanguage"/>
    /// is the language of the resolved project (or <c>null</c> when none was
    /// inferred); <paramref name="explicitLanguage"/> is the user-supplied
    /// <c>--language</c> value (or <c>null</c>). Both accept canonical labels and
    /// aliases.
    /// </summary>
    public async Task<TemplateVariantResolution> ResolveAsync(
        TemplateGroup group,
        string? ambientLanguage,
        string? explicitLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);

        // 1. Ambient signals break ties first.
        TemplateVariantResolution byAmbient = _resolver.Resolve(group, ambientLanguage);
        if (byAmbient is TemplateVariantResolution.Resolved)
        {
            return byAmbient;
        }

        // 2. --language breaks remaining ties. An explicit request is authoritative:
        //    a match resolves, and a request that fits no variant surfaces NoMatch
        //    instead of prompting.
        if (!string.IsNullOrWhiteSpace(explicitLanguage))
        {
            return _resolver.Resolve(group, explicitLanguage);
        }

        // 3. Neither ambient nor --language decided. Prompt only when a real choice
        //    remains: a single surviving variant resolves without asking, and an
        //    empty candidate set stays a NoMatch.
        IReadOnlyList<TemplateGroupVariant> candidates =
            byAmbient is TemplateVariantResolution.Ambiguous ambiguous
                ? ambiguous.Candidates
                : group.Variants;

        if (candidates.Count == 1)
        {
            return _resolver.Resolve(group, candidates[0].Language);
        }

        if (candidates.Count == 0)
        {
            return byAmbient;
        }

        return await PromptAsync(group, candidates, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TemplateVariantResolution> PromptAsync(
        TemplateGroup group,
        IReadOnlyList<TemplateGroupVariant> candidates,
        CancellationToken cancellationToken)
    {
        // Offer each surviving variant's own language label as a choice.
        List<string> choices = [.. candidates.Select(c => c.Language)];

        string selected = await _interaction
            .PromptForSelectionAsync("Select a language", choices, cancellationToken)
            .ConfigureAwait(false);

        // Narrow the group by the chosen language so the stack is derived the same
        // way as every other resolution path.
        return _resolver.Resolve(group, selected);
    }
}
