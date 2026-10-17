// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Workloads.Install;

/// <summary>
/// Owns privately extracted exact payloads and their verification evidence until commit or cancellation.
/// </summary>
internal sealed class PreparedWorkloadDeployment(TempDirectory staging, InspectedWorkloadPackage package, string source,
    WorkloadOwnershipKind ownership, LogicalPackage? logicalPackage, bool force, IReadOnlyDictionary<string, string> hashes,
    string expectedState) : IDisposable
{
    public string StagingPath => staging.Path;
    public InspectedWorkloadPackage Package { get; } = package;
    public string Source { get; } = source;
    public WorkloadOwnershipKind Ownership { get; } = ownership;
    public LogicalPackage? LogicalPackage { get; } = logicalPackage;
    public bool Force { get; } = force;
    public IReadOnlyDictionary<string, string> Hashes { get; } = hashes;
    public string ExpectedState { get; } = expectedState;
    public PreparedBundledTemplates? BundledTemplates { get; init; }
    public PreparedWorkloadReuse? UpdateFrom { get; init; }
    public PreparedWorkloadReuse? Destination { get; init; }
    public string CaptureState(IReadOnlyList<WorkloadEntry> installed)
        => WorkloadStateSnapshot.CaptureAffected(installed, Package.Identity.PackageId, LogicalPackage?.PackageId);
    public void Dispose()
    {
        BundledTemplates?.Dispose();
        UpdateFrom?.Dispose();
        Destination?.Dispose();
        staging.Dispose();
    }
}

internal static class WorkloadStateSnapshot
{
    public static string Capture(IEnumerable<WorkloadEntry> entries)
        => JsonSerializer.Serialize(new WorkloadRegistry
        {
            Workloads = [.. entries.OrderBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.PackageVersion, StringComparer.Ordinal)],
        }, WorkloadJsonContext.Default.WorkloadRegistry);

    public static bool Same(WorkloadEntry left, WorkloadEntry right) => Capture([left]) == Capture([right]);

    public static string CaptureAffected(IEnumerable<WorkloadEntry> entries, string packageId, string? logicalPackageId)
        => Capture(entries.Where(entry => string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            || (logicalPackageId is not null && entry.LogicalPackage is not null
                && string.Equals(entry.LogicalPackage.PackageId, logicalPackageId, StringComparison.OrdinalIgnoreCase))));
}
