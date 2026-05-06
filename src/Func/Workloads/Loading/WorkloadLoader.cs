// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Default <see cref="IWorkloadLoader"/>. Loads each entry into its own
/// collectible <see cref="WorkloadLoadContext"/> so dependency versions
/// don't collide across workloads.
/// </summary>
internal sealed class WorkloadLoader(IWorkloadPaths paths) : IWorkloadLoader
{
    private readonly IWorkloadPaths _paths = paths
        ?? throw new ArgumentNullException(nameof(paths));

    /// <inheritdoc />
    public IReadOnlyList<WorkloadInfo> Load(IReadOnlyList<WorkloadEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var results = new List<WorkloadInfo>(entries.Count);
        foreach (WorkloadEntry entry in entries)
        {
            results.Add(LoadEntry(entry));
        }

        return results;
    }

    private WorkloadInfo LoadEntry(WorkloadEntry entry)
    {
        string installPath = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        string contentRoot = Path.Combine(installPath, WorkloadMetadataReader.ContentDirectoryName);
        string assemblyPath = Path.Combine(contentRoot, entry.EntryPoint.AssemblyPath);

        if (!File.Exists(assemblyPath))
        {
            throw new GracefulException(
                $"[{entry.PackageId}] Could not load workload: assembly '{entry.EntryPoint.AssemblyPath}' was not found at '{contentRoot}'.",
                isUserError: true);
        }

        Assembly assembly = new WorkloadLoadContext(entry.PackageId, assemblyPath)
            .LoadFromAssemblyPath(assemblyPath);

        Type? type = assembly.GetType(entry.EntryPoint.Type, throwOnError: false) ?? throw new GracefulException(
                $"[{entry.PackageId}] Could not load workload: type '{entry.EntryPoint.Type}' was not found in '{entry.EntryPoint.AssemblyPath}' (install path: '{installPath}').",
                isUserError: true);

        if (!typeof(Workload).IsAssignableFrom(type))
        {
            throw new GracefulException(
                $"[{entry.PackageId}] Could not load workload: type '{entry.EntryPoint.Type}' does not derive from {nameof(Workload)} (install path: '{installPath}').",
                isUserError: true);
        }

        var instance = (Workload)Activator.CreateInstance(type)!;

        return new WorkloadInfo(
            Instance: instance,
            PackageId: entry.PackageId,
            PackageVersion: entry.PackageVersion,
            Aliases: entry.Aliases);
    }
}
