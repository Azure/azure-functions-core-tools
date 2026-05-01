// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Default <see cref="IWorkloadLoader"/>. Loads each entry into its own
/// collectible <see cref="WorkloadLoadContext"/> so dependency versions
/// don't collide across workloads.
/// </summary>
internal sealed class WorkloadLoader : IWorkloadLoader
{
    /// <inheritdoc />
    public IReadOnlyList<LoadedWorkload> LoadInstalled(IReadOnlyList<InstalledWorkload> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);

        var results = new List<LoadedWorkload>(installed.Count);
        foreach (var item in installed)
        {
            results.Add(LoadEntry(item));
        }

        return results;
    }

    private static LoadedWorkload LoadEntry(InstalledWorkload installed)
    {
        var entry = installed.Entry;
        var assemblyPath = Path.Combine(entry.InstallPath, entry.EntryPoint.Assembly);
        if (!File.Exists(assemblyPath))
        {
            throw new GracefulException(
                $"[{installed.PackageId}] Could not load workload: assembly '{entry.EntryPoint.Assembly}' was not found at '{entry.InstallPath}'.",
                isUserError: true);
        }

        var assembly = new WorkloadLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);

        var type = assembly.GetType(entry.EntryPoint.Type, throwOnError: false);
        if (type is null)
        {
            throw new GracefulException(
                $"[{installed.PackageId}] Could not load workload: type '{entry.EntryPoint.Type}' was not found in '{entry.EntryPoint.Assembly}' (install path: '{entry.InstallPath}').",
                isUserError: true);
        }

        if (!typeof(IWorkload).IsAssignableFrom(type))
        {
            throw new GracefulException(
                $"[{installed.PackageId}] Could not load workload: type '{entry.EntryPoint.Type}' does not implement {nameof(IWorkload)} (install path: '{entry.InstallPath}').",
                isUserError: true);
        }

        var instance = (IWorkload)Activator.CreateInstance(type)!;

        var info = new WorkloadInfo(
            PackageId: installed.PackageId,
            PackageVersion: installed.Version,
            DisplayName: entry.DisplayName,
            Description: entry.Description,
            Aliases: entry.Aliases);

        return new LoadedWorkload(instance, info);
    }
}
