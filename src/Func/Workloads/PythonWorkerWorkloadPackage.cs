// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Per-RID variant of the python worker content workload. The upstream
/// <c>Microsoft.Azure.Functions.PythonWorker</c> NuGet ships native binaries
/// for every platform, so we repack one workload nupkg per RID and resolve
/// the current platform's package id here. Mirrors <see cref="HostWorkloadPackage"/>.
/// </summary>
internal static class PythonWorkerWorkloadPackage
{
    internal const string PackageIdPrefix = "Azure.Functions.Cli.Workloads.Workers.Python.";

    /// <summary>
    /// RIDs we publish a python worker pack for. Must match the
    /// <c>_CliRuntimeIdentifiers</c> set in
    /// <c>src/Workloads/Workers/Python/Workloads.Workers.Python.csproj</c>.
    /// The upstream PythonWorker package has no <c>win-arm64</c> assets.
    /// </summary>
    public static readonly FrozenSet<string> SupportedRuntimeIdentifiers =
        new[] { "win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string CurrentPackageId => FromRuntimeIdentifier(WorkloadRuntimeIdentifier.Current);

    public static bool IsSupported(string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        return SupportedRuntimeIdentifiers.Contains(runtimeIdentifier.Trim());
    }

    public static bool IsCurrentRuntimeSupported() => IsSupported(WorkloadRuntimeIdentifier.Current);

    public static string FromRuntimeIdentifier(string runtimeIdentifier)
        => WorkloadRuntimeIdentifier.Qualify(PackageIdPrefix, runtimeIdentifier);
}

