// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Quickstart;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Quickstart provider for the .NET stack (C# only).
/// </summary>
internal sealed class DotNetQuickstartProvider : IQuickstartProvider
{
    public string Stack => "dotnet";

    public string DisplayName => ".NET";

    public IReadOnlyList<string> ManifestLanguages { get; } = ["CSharp"];

    public string GetDisplayLanguage(string manifestLanguage) => manifestLanguage switch
    {
        "CSharp" => "C#",
        _ => manifestLanguage
    };

    public string? ResolveManifestLanguage(string userInput) => userInput.ToLowerInvariant() switch
    {
        "c#" or "csharp" => "CSharp",
        _ => null
    };

    public IReadOnlyList<string> GetNextSteps(string manifestLanguage) =>
    [
        "Run `func run` to launch the app"
    ];
}
