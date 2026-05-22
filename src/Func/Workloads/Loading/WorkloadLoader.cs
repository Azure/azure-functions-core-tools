// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
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
    public IReadOnlyList<RuntimeWorkloadInfo> Load(IReadOnlyList<WorkloadEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var results = new List<RuntimeWorkloadInfo>(entries.Count);
        foreach (WorkloadEntry entry in entries)
        {
            results.Add(LoadEntry(entry));
        }

        return results;
    }

    private RuntimeWorkloadInfo LoadEntry(WorkloadEntry entry)
    {
        if (entry.Kind != WorkloadKind.Workload)
        {
            throw new InvalidOperationException(
                $"[{entry.PackageId}] WorkloadLoader was invoked for a non-runtime entry (kind={entry.Kind}). " +
                "Only kind=workload entries should reach the loader. This is a CLI bug.");
        }

        EntryPointSpec entryPoint = entry.EntryPoint
            ?? throw new InvalidOperationException(
                $"[{entry.PackageId}] WorkloadLoader was invoked for a runtime entry without an EntryPoint. " +
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
            throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: assembly path '{entryPoint.AssemblyPath}' resolves outside the package content root '{contentRoot}'.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: assembly '{entryPoint.AssemblyPath}' was not found at '{contentRoot}'.");
        }

        Assembly assembly = new WorkloadLoadContext(entry.PackageId, assemblyPath)
            .LoadFromAssemblyPath(assemblyPath);

        Type? type = assembly.GetType(entryPoint.Type, throwOnError: false) ?? throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: type '{entryPoint.Type}' was not found in '{entryPoint.AssemblyPath}' (install path: '{installPath}').");

        if (!typeof(Workload).IsAssignableFrom(type))
        {
            throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: type '{entryPoint.Type}' does not derive from {nameof(Workload)} (install path: '{installPath}').");
        }

        var instance = (Workload)Activator.CreateInstance(type)!;

        return new RuntimeWorkloadInfo(
            Instance: instance,
            PackageId: entry.PackageId,
            PackageVersion: entry.PackageVersion,
            Aliases: entry.Aliases,
            InstallDirectory: installPath,
            ContentRoot: contentRoot,
            DisplayName: instance.DisplayName,
            Description: instance.Description);
    }
}
