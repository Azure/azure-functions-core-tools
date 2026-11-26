// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Maps stack languages to the language tags that installed templates declare.
/// </summary>
/// <remarks>
/// Stacks identify a language by its canonical ID (for example <c>csharp</c>), which <c>.func/config.json</c> stores.
/// Templates tag their language with the display form (for example <c>C#</c>) in <c>template.json</c>. Commands
/// that select templates for a stack language use this mapping instead of passing either form directly.
/// </remarks>
internal static class TemplateLanguage
{
    /// <summary>
    /// Returns the template language tag for <paramref name="language"/>.
    /// </summary>
    public static string ToTemplateTag(LanguageDescriptor language)
    {
        ArgumentNullException.ThrowIfNull(language);

        // TODO: LanguageDescriptor has no template language tag, so the display name stands in for it. Every
        // shipped template tags its language with the display name; move to a descriptor property if one is added.
        return language.DisplayName;
    }
}
