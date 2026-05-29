// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Adapter over <see cref="IWorkloadStore"/> + <see cref="IWorkloadPaths"/> that
/// surfaces only the installed templates content-workload rows for a given
/// stack. Mirrors <c>InstalledBundleWorkloads</c>.
/// </summary>
internal sealed class InstalledTemplatesWorkloads(IWorkloadStore store, IWorkloadPaths paths)
    : IInstalledTemplatesWorkloads
{
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<IReadOnlyList<InstalledTemplatesWorkload>> ListInstalledAsync(
        string stack,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(stack))
        {
            throw new ArgumentException("Stack must be non-empty.", nameof(stack));
        }

        string expectedPackageId = TemplatesWorkloadConstants.GetPackageId(stack);
        IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync(cancellationToken);

        List<InstalledTemplatesWorkload> result = [];
        foreach (WorkloadEntry entry in entries)
        {
            if (entry.Kind != WorkloadKind.Content)
            {
                continue;
            }

            if (!string.Equals(entry.PackageId, expectedPackageId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string installDir = _paths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
            result.Add(new InstalledTemplatesWorkload(stack.Trim().ToLowerInvariant(), entry.PackageVersion, installDir));
        }

        return result;
    }
}
