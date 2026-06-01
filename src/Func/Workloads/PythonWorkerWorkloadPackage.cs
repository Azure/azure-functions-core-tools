// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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

    public static string CurrentPackageId => FromRuntimeIdentifier(WorkloadRuntimeIdentifier.Current);

    public static string FromRuntimeIdentifier(string runtimeIdentifier)
        => WorkloadRuntimeIdentifier.Qualify(PackageIdPrefix, runtimeIdentifier);
}
