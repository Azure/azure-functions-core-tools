// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.DotNet;

internal sealed class DotNetProjectStack : IProjectStack
{
    public string Stack => "dotnet";

    public string DisplayName => ".NET";

    public IReadOnlyList<string> WorkerRuntimeAliases { get; } = ["dotnet-isolated"];

    public IReadOnlyList<string> SupportedLanguages => [.. SupportedLanguageAliases.Keys];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> SupportedLanguageAliases { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        { "C#", ["csharp"] },
        { "F#", ["fsharp"] }
    };
}
