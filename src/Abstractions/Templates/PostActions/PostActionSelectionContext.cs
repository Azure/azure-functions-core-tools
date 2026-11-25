// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Describes the selected template for workload post-action applicability.
/// </summary>
public sealed class PostActionSelectionContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PostActionSelectionContext"/> class.
    /// </summary>
    /// <param name="stack">The selected template's stack tag.</param>
    /// <param name="language">The selected template's language tag.</param>
    /// <exception cref="ArgumentNullException">A tag is null.</exception>
    public PostActionSelectionContext(string stack, string language)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(language);
        Stack = stack.Trim();
        Language = language.Trim();
    }

    /// <summary>
    /// Gets the trimmed stack tag, preserving its casing without resolving aliases; empty means the tag was absent or blank.
    /// </summary>
    public string Stack { get; }

    /// <summary>
    /// Gets the trimmed language tag, preserving its casing without resolving aliases; empty means the tag was absent or blank.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// Determines whether the selected template targets the given stack and one of its languages, ignoring case.
    /// </summary>
    /// <param name="stack">The stack to compare against.</param>
    /// <returns>True when the stack ID matches and the language is a supported language or alias.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stack"/> is null.</exception>
    public bool IsCompatibleWith(IProjectStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        if (Language.Length == 0 || !string.Equals(Stack, stack.Stack, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return stack.SupportedLanguages.Contains(Language, StringComparer.OrdinalIgnoreCase)
            || stack.SupportedLanguageAliases.Any(language =>
                string.Equals(Language, language.Key, StringComparison.OrdinalIgnoreCase)
                || language.Value.Contains(Language, StringComparer.OrdinalIgnoreCase));
    }
}
