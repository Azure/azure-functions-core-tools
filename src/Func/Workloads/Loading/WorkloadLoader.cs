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
        string assemblyPath = ResolveAssemblyPath(installPath, entryPoint.AssemblyPath, entry.PackageId);
        string contentRoot = Path.GetDirectoryName(assemblyPath)!;

        // Defense-in-depth: the metadata reader already rejects rooted paths
        // and `..` segments, but the on-disk registry stores the same value
        // and could be tampered with. Refuse anything resolving outside
        // the install directory (where workload.json lives).
        string installPathWithSeparator = installPath.EndsWith(Path.DirectorySeparatorChar)
            ? installPath
            : installPath + Path.DirectorySeparatorChar;
        if (!assemblyPath.StartsWith(installPathWithSeparator, StringComparison.Ordinal))
        {
            throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: assembly path '{entryPoint.AssemblyPath}' resolves outside the install directory '{installPath}'.");
        }

        if (!File.Exists(assemblyPath))
        {
            throw new InvalidWorkloadException(
                $"[{entry.PackageId}] Could not load workload: assembly '{entryPoint.AssemblyPath}' was not found at '{installPath}'.");
        }

        WorkloadLoadContext loadContext = new(entry.PackageId, assemblyPath);
        Assembly assembly = loadContext.LoadFromAssemblyPath(assemblyPath);

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
            Description: instance.Description,
            LoadContext: loadContext);
    }

    /// <summary>
    /// Resolves the entry-point assembly path, first relative to the install directory
    /// (where workload.json lives), then falling back to the legacy tools/any/ subdirectory.
    /// </summary>
    private static string ResolveAssemblyPath(string installPath, string relativeAssemblyPath, string packageId)
    {
        // Try resolving directly relative to the install root (workload.json location).
        string candidate = Path.GetFullPath(Path.Combine(installPath, relativeAssemblyPath));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        // Fall back to legacy tools/any/ layout.
        string legacyCandidate = Path.GetFullPath(Path.Combine(installPath, "tools", "any", relativeAssemblyPath));
        if (File.Exists(legacyCandidate))
        {
            return legacyCandidate;
        }

        // Return the primary candidate so the caller produces a clear error message.
        return candidate;
    }
}
