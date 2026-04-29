// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Static catalog of workloads the CLI knows how to suggest installing when no
/// suitable workload is registered at runtime. This is **not** the registry of
/// what is currently installed — that comes from DI'd workload contributions.
/// It exists purely to power install hints (\"Install a stack to do X: <code>func workload install dotnet</code>\")
/// and will be replaced by the workload loader's manifest once that lands.
/// </summary>
internal static class KnownInstallableWorkloads
{
    /// <summary>
    /// Map of workload identifier (the value passed to <c>func workload install</c>)
    /// to the human-readable language list shown in install hints.
    /// </summary>
    public static readonly FrozenDictionary<string, string[]> LanguageMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet"] = ["C#", "F#"],
            ["node"] = ["JavaScript", "TypeScript"],
            ["python"] = ["Python"],
            ["java"] = ["Java"],
            ["powershell"] = ["PowerShell"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
