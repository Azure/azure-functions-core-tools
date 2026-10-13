// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

internal sealed class NodeProjectStack : IProjectStack
{
    public string Stack => "node";

    public string DisplayName => "Node.js";

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        { "JavaScript", ["js"] },
        { "TypeScript", ["ts"] }
    };
}
