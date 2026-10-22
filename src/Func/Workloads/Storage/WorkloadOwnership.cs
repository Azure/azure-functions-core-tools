// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

internal enum WorkloadOwnershipKind
{
    Explicit,
    Logical,
}

internal sealed record WorkloadOwnershipRemovalResult(bool OwnershipRemoved, bool EntryRemoved, WorkloadEntry? Entry);

internal sealed record WorkloadOwnershipMoveResult(WorkloadEntry Entry, bool PreviousEntryRemoved);

internal static class WorkloadOwnershipRules
{
    public static void ValidateDestination(WorkloadEntry? current, LogicalPackage? incoming, WorkloadOwnershipKind ownership)
    {
        if (!Enum.IsDefined(ownership))
        {
            throw new ArgumentOutOfRangeException(nameof(ownership));
        }

        if (ownership == WorkloadOwnershipKind.Logical && incoming is null)
        {
            throw new ArgumentException("Logical ownership requires logical package metadata.", nameof(incoming));
        }

        if (ownership == WorkloadOwnershipKind.Logical && current?.LogicalPackage is { } existing
            && (!string.Equals(existing.PackageId, incoming!.PackageId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(existing.PackageVersion, incoming.PackageVersion, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Physical workload '{current.PackageId}' version '{current.PackageVersion}' is already owned by logical package " +
                $"'{existing.PackageId}' version '{existing.PackageVersion}'.");
        }
    }
}
