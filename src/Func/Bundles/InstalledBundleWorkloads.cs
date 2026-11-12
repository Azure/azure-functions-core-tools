// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Bundles;

/// <summary>
/// Adapter over <see cref="IWorkloadStore"/> that surfaces only the installed bundle workload rows.
/// </summary>
internal sealed class InstalledBundleWorkloads(IWorkloadStore store) : IInstalledBundleWorkloads
{
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<IReadOnlyList<InstalledBundleWorkload>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync(cancellationToken);

        List<InstalledBundleWorkload> result = [];
        foreach (WorkloadEntry entry in entries)
        {
            if (entry.Kind != WorkloadKind.Content)
            {
                continue;
            }

            if (!string.Equals(entry.PackageId, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new InstalledBundleWorkload(entry.PackageVersion, entry.PackageRoot));
        }

        return result;
    }
}
