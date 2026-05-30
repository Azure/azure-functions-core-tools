// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Static catalog of stacks the CLI knows how to suggest setting up when no
/// suitable workload is registered at runtime. This is **not** the registry of
/// what is currently installed, that comes from DI'd workload contributions.
/// It exists purely to power setup hints (e.g. <c>func setup --features dotnet</c>)
/// and will be replaced by the workload loader's manifest once that lands.
/// </summary>
internal static class KnownInstallableWorkloads
{
    /// <summary>
    /// Map of stack identifier (the value passed to <c>func setup --features</c>)
    /// to the human-readable language list shown in setup hints.
    /// </summary>
    public static readonly FrozenDictionary<string, string[]> LanguageMap =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet"] = ["C#", "F#"],
            ["node"] = ["JavaScript", "TypeScript"],
            ["python"] = ["Python"],
            ["go"] = ["Go"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
