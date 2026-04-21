// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;

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
    /// Writes the workload install hints for all known runtimes.
    /// </summary>
    public static void WriteWorkloadInstallHints(Console.IInteractionService interaction)
    {
        foreach (var (worker, languages) in LanguageMap)
        {
            var langList = string.Join(", ", languages);
            var padded = $"func workload install {worker}".PadRight(38);
            interaction.WriteMarkupLine($"  [white]{padded}[/][grey]{langList}[/]");
        }
    }
}
