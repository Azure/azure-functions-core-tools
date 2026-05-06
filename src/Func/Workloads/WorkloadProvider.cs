// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Loading;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Default <see cref="IWorkloadProvider"/>. Reads the registry through
/// <see cref="IWorkloadStore"/>, hydrates each entry through
/// <see cref="IWorkloadLoader"/>, and caches the resulting list for the
/// lifetime of the provider (singleton in DI).
/// </summary>
internal sealed class WorkloadProvider(IWorkloadStore store, IWorkloadLoader loader) : IWorkloadProvider
{
    private readonly IWorkloadStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IWorkloadLoader _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private IReadOnlyList<WorkloadInfo>? _cached;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<WorkloadInfo>> GetWorkloadsAsync(
        CancellationToken cancellationToken = default)
    {
        // Fast path: already materialized.
        IReadOnlyList<WorkloadInfo>? snapshot = _cached;
        if (snapshot is not null)
        {
            return snapshot;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Double-checked: another caller may have populated the cache while
            // we waited on the gate.
            if (_cached is not null)
            {
                return _cached;
            }

            IReadOnlyList<WorkloadEntry> entries = await _store.GetWorkloadsAsync(cancellationToken);
            _cached = _loader.Load(entries);
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }
}
