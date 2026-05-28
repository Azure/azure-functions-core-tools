// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Quickstart provider for the Node stack (JavaScript and TypeScript).
/// </summary>
internal sealed class NodeQuickstartProvider : IQuickstartProvider
{
    public string Stack => "node";

    public string DisplayName => "Node";

    public IReadOnlyList<string> ManifestLanguages { get; } = ["TypeScript", "JavaScript"];

    public string GetDisplayLanguage(string manifestLanguage) => manifestLanguage;

    public string? ResolveManifestLanguage(string userInput) => userInput.ToLowerInvariant() switch
    {
        "javascript" or "js" => "JavaScript",
        "typescript" or "ts" => "TypeScript",
        _ => null
    };

    public IReadOnlyList<string> GetNextSteps(string manifestLanguage) =>
    [
        "Run `npm install` to install dependencies",
        "Run `func run` to launch the app"
    ];
}
