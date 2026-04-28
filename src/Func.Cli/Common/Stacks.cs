// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Central registry of stacks (language / runtime targets) and their supported languages.
/// </summary>
internal static class Stacks
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
}
