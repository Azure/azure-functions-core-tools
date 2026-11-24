// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
}
