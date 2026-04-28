// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Loading;

/// <summary>
/// Hydrates installed workloads from the global manifest into runnable
/// <see cref="LoadedWorkload"/> instances. Each workload is loaded into
/// its own <see cref="System.Runtime.Loader.AssemblyLoadContext"/> so
/// dependency versions don't collide across workloads.
/// </summary>
internal sealed class WorkloadLoader
{
    public Task<IReadOnlyList<LoadedWorkload>> LoadInstalledAsync(
        GlobalManifest manifest,
        CancellationToken cancellationToken)
    {
        var results = new List<LoadedWorkload>(manifest.Workloads.Count);
        foreach (var entry in manifest.Workloads)
        {
            results.Add(LoadEntry(entry));
        }

        return Task.FromResult<IReadOnlyList<LoadedWorkload>>(results);
    }

    private static LoadedWorkload LoadEntry(GlobalManifestEntry entry)
    {
        var assemblyPath = Path.Combine(entry.InstallPath, entry.EntryPoint.Assembly);
        if (!File.Exists(assemblyPath))
        {
            throw new GracefulException(
                $"Workload '{entry.PackageId}' could not be loaded: assembly '{entry.EntryPoint.Assembly}' was not found at '{entry.InstallPath}'.",
                isUserError: true);
        }

        var assembly = new WorkloadLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
        var type = assembly.GetType(entry.EntryPoint.Type, throwOnError: false);
        if (type is null)
        {
            throw new GracefulException(
                $"Workload '{entry.PackageId}' could not be loaded: type '{entry.EntryPoint.Type}' was not found in '{entry.EntryPoint.Assembly}'.",
                isUserError: true);
        }

        if (!typeof(IWorkload).IsAssignableFrom(type))
        {
            throw new GracefulException(
                $"Workload '{entry.PackageId}' could not be loaded: type '{entry.EntryPoint.Type}' does not implement {nameof(IWorkload)}.",
                isUserError: true);
        }

        var instance = (IWorkload)Activator.CreateInstance(type)!;

        var info = new WorkloadInfo(
            PackageId: entry.PackageId,
            PackageVersion: entry.Version,
            DisplayName: entry.DisplayName,
            Description: entry.Description,
            Aliases: entry.Aliases);

        return new LoadedWorkload(instance, info);
    }
}
