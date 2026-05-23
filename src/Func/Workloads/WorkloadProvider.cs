// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Default <see cref="IWorkloadProvider"/>. Snapshots the workload inventory
/// registered by <see cref="Hosting.WorkloadRegistration"/> into stable lists.
/// </summary>
internal sealed class WorkloadProvider(IEnumerable<WorkloadInfo> workloads) : IWorkloadProvider
{
    private readonly Snapshot _snapshot = new(workloads);

    public IReadOnlyList<WorkloadInfo> GetWorkloads() => _snapshot.Workloads;

    public IReadOnlyList<RuntimeWorkloadInfo> GetRuntimeWorkloads() => _snapshot.RuntimeWorkloads;

    public IReadOnlyList<RuntimeWorkloadInfo> GetRuntimeWorkloadsByPackageId(string packageId) =>
        _snapshot.GetRuntimeWorkloadsByPackageId(packageId);

    public IReadOnlyList<ContentWorkloadInfo> GetContentWorkloads() => _snapshot.ContentWorkloads;

    public IReadOnlyList<ContentWorkloadInfo> GetContentWorkloadsByPackageId(string packageId) =>
        _snapshot.GetContentWorkloadsByPackageId(packageId);

    private sealed class Snapshot
    {
        private static readonly IReadOnlyList<RuntimeWorkloadInfo> _emptyRuntimeWorkloads = [];
        private static readonly IReadOnlyList<ContentWorkloadInfo> _emptyContentWorkloads = [];

        private readonly Lazy<IReadOnlyList<RuntimeWorkloadInfo>> _runtimeWorkloads;
        private readonly Lazy<IReadOnlyList<ContentWorkloadInfo>> _contentWorkloads;
        private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<RuntimeWorkloadInfo>>> _runtimeWorkloadsByPackageId;
        private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<ContentWorkloadInfo>>> _contentWorkloadsByPackageId;

        public Snapshot(IEnumerable<WorkloadInfo> workloads)
        {
            ArgumentNullException.ThrowIfNull(workloads);

            Workloads = [.. workloads];
            _runtimeWorkloads = new(() => [.. Workloads.OfType<RuntimeWorkloadInfo>()]);
            _contentWorkloads = new(() => [.. Workloads.OfType<ContentWorkloadInfo>()]);
            _runtimeWorkloadsByPackageId = new(() => CreatePackageIdLookup(RuntimeWorkloads));
            _contentWorkloadsByPackageId = new(() => CreatePackageIdLookup(ContentWorkloads));
        }

        public IReadOnlyList<WorkloadInfo> Workloads { get; }

        public IReadOnlyList<RuntimeWorkloadInfo> RuntimeWorkloads => _runtimeWorkloads.Value;

        public IReadOnlyList<ContentWorkloadInfo> ContentWorkloads => _contentWorkloads.Value;

        public IReadOnlyList<RuntimeWorkloadInfo> GetRuntimeWorkloadsByPackageId(string packageId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

            return _runtimeWorkloadsByPackageId.Value.TryGetValue(packageId, out IReadOnlyList<RuntimeWorkloadInfo>? matching)
                ? matching
                : _emptyRuntimeWorkloads;
        }

        public IReadOnlyList<ContentWorkloadInfo> GetContentWorkloadsByPackageId(string packageId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

            return _contentWorkloadsByPackageId.Value.TryGetValue(packageId, out IReadOnlyList<ContentWorkloadInfo>? matching)
                ? matching
                : _emptyContentWorkloads;
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<TWorkload>> CreatePackageIdLookup<TWorkload>(
            IReadOnlyList<TWorkload> workloads)
            where TWorkload : WorkloadInfo
        {
            Dictionary<string, IReadOnlyList<TWorkload>> lookup = new(StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, TWorkload> group in workloads.GroupBy(w => w.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                lookup.Add(group.Key, [.. group]);
            }

            return lookup;
        }
    }
}
