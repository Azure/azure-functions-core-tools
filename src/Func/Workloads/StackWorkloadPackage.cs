// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics.CodeAnalysis;

namespace Azure.Functions.Cli.Workloads;

internal static class StackWorkloadPackage
{
    private const string PackageIdPrefix = "Azure.Functions.Cli.Workloads.";

    // Maps a profile `supportedRuntimes` entry to the stack workload package id.
    // Runtimes that don't have a corresponding stack workload (java, powershell,
    // custom, dotnet in-proc) are intentionally absent and skipped silently by callers.
    private static readonly IReadOnlyDictionary<string, string> _runtimeToStackName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["node"] = "Node",
            ["python"] = "Python",
            ["go"] = "Go",
            ["dotnet-isolated"] = "DotNet",
        };

    public static bool TryGetPackageId(string runtime, [NotNullWhen(true)] out string? packageId)
    {
        if (!string.IsNullOrWhiteSpace(runtime)
            && _runtimeToStackName.TryGetValue(runtime.Trim(), out string? stackName))
        {
            packageId = PackageIdPrefix + stackName;
            return true;
        }

        packageId = null;
        return false;
    }
}
