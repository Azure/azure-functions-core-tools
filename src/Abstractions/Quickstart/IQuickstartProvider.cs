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
/// When <see cref="SupportedLanguages"/> contains more than one entry, the
/// command prompts the user to pick a sub-language (e.g. .NET → C#/F#,
/// Node → JavaScript/TypeScript).
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
    /// Manifest-level language values this stack handles (e.g. ["CSharp", "FSharp"]).
    /// When the list has more than one entry, the command prompts for a sub-language.
    /// </summary>
    public IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>
    /// Returns post-scaffold "next steps" for the given language
    /// (e.g. "npm install", "pip install -r requirements.txt", "func start").
    /// </summary>
    public IReadOnlyList<string> GetNextSteps(string language);
}
