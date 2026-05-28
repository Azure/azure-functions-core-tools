// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Declares a workload's participation in the <c>func quickstart</c> flow.
/// Each stack that supports quickstart scaffolding registers an implementation
/// with DI; the command discovers all providers and uses them to drive
/// language selection and post-scaffold guidance.
/// </summary>
/// <remarks>
/// Available languages are derived from the manifest at runtime. The provider
/// declares which manifest language values it handles via
/// <see cref="ManifestLanguages"/> and owns the bidirectional mapping between
/// user-facing names (matching <c>func init</c>) and manifest values.
/// </remarks>
public interface IQuickstartProvider
{
    /// <summary>
    /// The canonical stack id (e.g. "dotnet", "node", "python").
    /// </summary>
    public string Stack { get; }

    /// <summary>
    /// Human-friendly label shown in the interactive runtime prompt
    /// (e.g. ".NET", "Node", "Python").
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Manifest <see cref="QuickstartEntry.Language"/> values this provider
    /// handles, in preferred display order (e.g. ["CSharp"] for dotnet,
    /// ["TypeScript", "JavaScript"] for node). The command intersects these
    /// with the manifest to determine which languages are actually available,
    /// preserving the order declared here.
    /// </summary>
    public IReadOnlyList<string> ManifestLanguages { get; }

    /// <summary>
    /// Maps a manifest language value to a user-friendly display name
    /// (e.g. "CSharp" → "C#"). Returns <paramref name="manifestLanguage"/>
    /// unchanged when no special mapping is needed.
    /// </summary>
    public string GetDisplayLanguage(string manifestLanguage);

    /// <summary>
    /// Resolves user input (canonical name or alias, e.g. "c#", "csharp", "js")
    /// to the corresponding manifest language value (e.g. "CSharp", "JavaScript").
    /// Returns <c>null</c> when the input is not recognized by this provider.
    /// Accepted values match <c>func init --language</c> for UX consistency.
    /// </summary>
    public string? ResolveManifestLanguage(string userInput);

    /// <summary>
    /// Returns post-scaffold "next steps" for the given manifest language
    /// (e.g. "npm install", "pip install -r requirements.txt", "func start").
    /// </summary>
    public IReadOnlyList<string> GetNextSteps(string manifestLanguage);
}
