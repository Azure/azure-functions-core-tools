// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Well-known func template tag keys and pure tag-reading helpers over
/// <see cref="ITemplateInfo.TagsCollection"/>.
/// </summary>
internal static class FuncTemplateTags
{
    internal const string Stack = "azfunc-stack";
    internal const string Trigger = "azfunc-trigger";
    internal const string Language = "language";
    internal const string Type = "type";
    internal const string ItemType = "item";
    internal const string ExtensionBundleConstraintType = "func-extension-bundle";

    /// <summary>
    /// Returns the value of <paramref name="key"/> or <c>null</c> when the tag
    /// is absent.
    /// </summary>
    internal static string? Tag(this ITemplateInfo template, string key)
        => template.TagsCollection.TryGetValue(key, out string? value) ? value : null;

    /// <summary>
    /// True when the template's <c>azfunc-stack</c> tag matches
    /// <paramref name="stack"/> (case-insensitive).
    /// </summary>
    internal static bool MatchesStack(this ITemplateInfo template, string stack)
        => string.Equals(template.Tag(Stack), stack, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the template's <c>language</c> tag matches
    /// <paramref name="language"/> (case-insensitive), or when no language
    /// filter was requested.
    /// </summary>
    internal static bool MatchesLanguage(this ITemplateInfo template, string? language)
        => string.IsNullOrWhiteSpace(language)
        || string.Equals(template.Tag(Language), language, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the template is a function item template (as opposed to a
    /// full project template).
    /// </summary>
    internal static bool IsItemTemplate(this ITemplateInfo template)
        => string.Equals(template.Tag(Type), ItemType, StringComparison.OrdinalIgnoreCase);
}
