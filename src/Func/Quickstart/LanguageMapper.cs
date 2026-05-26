// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Maps CLI language flag values to manifest language strings and display labels.
/// </summary>
internal static class LanguageMapper
{
    // `csharp`, `fsharp` etc. map directly. `dotnet` and `node` are runtime
    // aliases that map to a default language for non-interactive use
    // (matching `func init` behaviour): `--language dotnet` resolves to CSharp,
    // `--language node` to TypeScript, with no sub-prompt. Interactive mode
    // (no --language flag) lets the user pick from any manifest language.
    private static readonly Dictionary<string, string> _flagToManifestLanguage =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp"] = "CSharp",
            ["fsharp"] = "FSharp",
            ["javascript"] = "JavaScript",
            ["typescript"] = "TypeScript",
            ["python"] = "Python",
            ["java"] = "Java",
            ["powershell"] = "PowerShell",
            ["dotnet"] = "CSharp",
            ["node"] = "TypeScript",
        };

    private static readonly Dictionary<string, string> _runtimeToDefaultLanguage =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet"] = "CSharp",
            ["node"] = "TypeScript",
            ["python"] = "Python",
            ["java"] = "Java",
            ["powershell"] = "PowerShell",
        };

    /// <summary>
    /// Returns the manifest language string for the given CLI flag value,
    /// or <see langword="null"/> if the flag value is not recognised.
    /// </summary>
    public static string? ToManifestLanguage(string flagValue) =>
        _flagToManifestLanguage.TryGetValue(flagValue, out string? lang) ? lang : null;

    /// <summary>
    /// Returns a list of selectable manifest language strings for the specified
    /// runtime (e.g. <c>"dotnet"</c> returns <c>["CSharp", "FSharp"]</c>,
    /// <c>"node"</c> returns <c>["TypeScript", "JavaScript"]</c>).
    /// </summary>
    public static IReadOnlyList<string> GetLanguagesForRuntime(string runtime) =>
        runtime.ToLowerInvariant() switch
        {
            "dotnet" => ["CSharp", "FSharp"],
            "node" => ["TypeScript", "JavaScript"],
            "python" => ["Python"],
            "java" => ["Java"],
            "powershell" => ["PowerShell"],
            _ => [],
        };

    /// <summary>
    /// Returns the default manifest language for the specified runtime.
    /// </summary>
    public static string? DefaultLanguageForRuntime(string runtime) =>
        _runtimeToDefaultLanguage.TryGetValue(runtime, out string? lang) ? lang : null;

    /// <summary>
    /// All supported manifest language strings.
    /// </summary>
    public static IReadOnlyList<string> AllManifestLanguages =>
        [.. _flagToManifestLanguage.Values.Distinct(StringComparer.OrdinalIgnoreCase)];
}
