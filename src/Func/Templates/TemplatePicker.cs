// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Interactive template picker for <c>func new</c>. Wraps
/// <see cref="IInteractionService.PromptForSelectionAsync"/> with a
/// templates-specific presentation (display name + trigger kind).
/// </summary>
internal sealed class TemplatePicker(IInteractionService interaction)
{
    private readonly IInteractionService _interaction =
        interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <summary>
    /// Prompts the user to pick one of <paramref name="candidates"/>. Returns
    /// the chosen template. The caller is responsible for guarding against
    /// non-interactive mode before calling this — picker invocation in a
    /// non-interactive shell will block waiting for stdin.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="candidates"/> is empty.
    /// </exception>
    public async Task<FunctionTemplateInfo> PickAsync(
        IReadOnlyList<FunctionTemplateInfo> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("Candidate list must be non-empty.", nameof(candidates));
        }

        // Display strings: prefer "<DisplayName> (<id>)" so the user sees both
        // the friendly name and the value that ends up on the command line.
        Dictionary<string, FunctionTemplateInfo> byDisplay = new(StringComparer.Ordinal);
        List<string> display = new(candidates.Count);
        foreach (FunctionTemplateInfo template in candidates)
        {
            string key = string.IsNullOrWhiteSpace(template.DisplayName)
                ? template.Id
                : $"{template.DisplayName} ({template.Id})";

            // Defensive dedup: if two templates produce the same display
            // string, the registry walker should have caught the collision
            // upstream — fall back to the id-only form for the second entry.
            if (!byDisplay.TryAdd(key, template))
            {
                byDisplay[template.Id] = template;
                display.Add(template.Id);
            }
            else
            {
                display.Add(key);
            }
        }

        string picked = await _interaction.PromptForSelectionAsync(
            "Select a template:", display, cancellationToken);

        return byDisplay.TryGetValue(picked, out FunctionTemplateInfo? chosen)
            ? chosen
            : candidates[0];
    }
}
