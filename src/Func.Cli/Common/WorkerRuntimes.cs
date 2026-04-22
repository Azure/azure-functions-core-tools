// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using Azure.Functions.Cli.Console;

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Central registry of worker runtimes and their supported languages.
/// </summary>
public static class WorkerRuntimes
{
    public static readonly FrozenDictionary<string, string[]> LanguageMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet"] = ["C#", "F#"],
            ["node"] = ["JavaScript", "TypeScript"],
            ["python"] = ["Python"],
            ["java"] = ["Java"],
            ["powershell"] = ["PowerShell"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the workload install hints for all known runtimes as an aligned list.
    /// </summary>
    public static void WriteWorkloadInstallHints(IInteractionService interaction)
    {
        var items = LanguageMap.Select(static kvp => new DefinitionItem($"func workload install {kvp.Key}", string.Join(", ", kvp.Value)));

        interaction.WriteDefinitionList(items);
    }
}
