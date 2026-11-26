// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Storage;

internal static class WorkloadActivation
{
    public static bool IsCompatible(string? targetRid, string currentRid)
        => targetRid is null || string.Equals(targetRid, currentRid, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Selects the highest runtime workload version per physical package. Older versions
    /// may still be installed for rollback but do not contribute stack capabilities.
    /// </summary>
    public static IReadOnlyList<WorkloadEntry> SelectLiveRuntimeEntries(IEnumerable<WorkloadEntry> compatibleEntries)
        => [.. compatibleEntries.Where(e => e.Kind == WorkloadKind.Workload)
            .GroupBy(e => e.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(e => ParseVersionOrZero(e.PackageVersion))!)];

    public static NuGetVersion ParseVersionOrZero(string version)
        => NuGetVersion.TryParse(version, out NuGetVersion? parsed) ? parsed : new NuGetVersion(0, 0, 0);
}
