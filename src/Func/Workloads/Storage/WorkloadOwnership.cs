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
