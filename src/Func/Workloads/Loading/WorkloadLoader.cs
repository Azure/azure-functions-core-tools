// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using Azure.Functions.Cli.Common;
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
        // Non-workload entries are filtered upstream; reaching the loader
        // without an EntryPoint is a CLI bug, not a user error.
        EntryPointSpec entryPoint = entry.EntryPoint
            ?? throw new InvalidOperationException(
                $"[{entry.PackageId}] WorkloadLoader was invoked for a non-workload entry (kind={entry.Kind}, no EntryPoint). " +
                "Only kind=workload entries should reach the loader. This is a CLI bug.");

        string installPath = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        string contentRoot = Path.GetFullPath(Path.Combine(installPath, "tools", "any"));
        string assemblyPath = Path.GetFullPath(Path.Combine(contentRoot, entryPoint.AssemblyPath));

        // Defense-in-depth: the metadata reader already rejects rooted paths
        // and `..` segments, but the on-disk registry stores the same value
        // and could be tampered with. Refuse anything resolving outside
        // the content root.
        string contentRootWithSeparator = contentRoot.EndsWith(Path.DirectorySeparatorChar)
            ? contentRoot
            : contentRoot + Path.DirectorySeparatorChar;
        if (!assemblyPath.StartsWith(contentRootWithSeparator, StringComparison.Ordinal))
        {
            throw new GracefulException(
                $"[{entry.PackageId}] Could not load workload: assembly path '{entryPoint.AssemblyPath}' resolves outside the package content root '{contentRoot}'.",
                isUserError: true);
        }

        if (!File.Exists(assemblyPath))
        {
            throw new GracefulException(
                $"[{entry.PackageId}] Could not load workload: assembly '{entryPoint.AssemblyPath}' was not found at '{contentRoot}'.",
                isUserError: true);
        }

        Assembly assembly = new WorkloadLoadContext(entry.PackageId, assemblyPath)
            .LoadFromAssemblyPath(assemblyPath);

        Type? type = assembly.GetType(entryPoint.Type, throwOnError: false) ?? throw new GracefulException(
                $"[{entry.PackageId}] Could not load workload: type '{entryPoint.Type}' was not found in '{entryPoint.AssemblyPath}' (install path: '{installPath}').",
                isUserError: true);

        if (!typeof(Workload).IsAssignableFrom(type))
        {
            throw new GracefulException(
                $"[{entry.PackageId}] Could not load workload: type '{entryPoint.Type}' does not derive from {nameof(Workload)} (install path: '{installPath}').",
                isUserError: true);
        }

        var instance = (Workload)Activator.CreateInstance(type)!;

        return new WorkloadInfo(
            Instance: instance,
            PackageId: entry.PackageId,
            PackageVersion: entry.PackageVersion,
            Aliases: entry.Aliases,
            DisplayName: instance.DisplayName,
            Description: instance.Description);
    }
}
