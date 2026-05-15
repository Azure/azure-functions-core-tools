// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.CodeAnalysis.Diagnostics;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.Common;

/// <summary>
/// Shared metadata keys and matchers for AdditionalFiles consumed by the template generators.
/// </summary>
internal static class AdditionalFileMetadata
{
    public const string TemplateVersionKey = "build_metadata.AdditionalFiles.TemplateVersion";
    public const string TemplateLanguageKey = "build_metadata.AdditionalFiles.TemplateLanguage";
    public const string TemplateAssetKindKey = "build_metadata.AdditionalFiles.TemplateAssetKind";

    public const string AssetKindTemplates = "templates";
    public const string AssetKindBindings = "bindings";

    public static bool HasTemplateVersion(AnalyzerConfigOptions options, string expected)
        => options.TryGetValue(TemplateVersionKey, out string? value)
            && value.Equals(expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true if the AdditionalFile has the expected asset kind. Missing metadata defaults
    /// to <see cref="AssetKindTemplates"/> for back-compat with entries that only set TemplateVersion.
    /// </summary>
    public static bool IsAssetKind(AnalyzerConfigOptions options, string expected)
    {
        if (!options.TryGetValue(TemplateAssetKindKey, out string? value) || string.IsNullOrEmpty(value))
        {
            return string.Equals(expected, AssetKindTemplates, StringComparison.OrdinalIgnoreCase);
        }

        return value.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetTemplateLanguage(AnalyzerConfigOptions options)
        => options.TryGetValue(TemplateLanguageKey, out string? value) ? value : null;
}
