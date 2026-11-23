// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Preserves compatibility with installed workloads built before the Quickstart command was removed.
/// </summary>
public interface IQuickstartProvider
{
    /// <summary>
    /// Gets the canonical stack identifier.
    /// </summary>
    public string Stack { get; }

    /// <summary>
    /// Gets the display name for the stack.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the supported manifest languages.
    /// </summary>
    public IReadOnlyList<string> ManifestLanguages { get; }

    /// <summary>
    /// Gets the display name for a manifest language.
    /// </summary>
    public string GetDisplayLanguage(string manifestLanguage);

    /// <summary>
    /// Resolves user input to a manifest language.
    /// </summary>
    public string? ResolveManifestLanguage(string userInput);

    /// <summary>
    /// Gets post-scaffold guidance for a manifest language.
    /// </summary>
    public IReadOnlyList<string> GetNextSteps(string manifestLanguage);
}
