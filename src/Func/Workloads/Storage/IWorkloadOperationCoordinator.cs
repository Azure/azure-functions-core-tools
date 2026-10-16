// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Storage;

/// <summary>
/// Admits participating writers for one physical workload store. Acquire only after private preparation and interaction.
/// </summary>
internal interface IWorkloadOperationCoordinator
{
    public Task<IWorkloadOperation> AcquireAsync(CancellationToken cancellationToken, Action? waiting = null);
}

/// <summary>
/// Carries live writer authority explicitly through deployment and registry effects.
/// Dispose template sessions and settle their recovery before releasing this operation.
/// </summary>
internal interface IWorkloadOperation : IDisposable
{
    public bool IsDirty { get; }
    public void Validate(IWorkloadPaths paths);
    public void BeginMutation();
    public void CompleteMutation();
}

internal sealed class WorkloadRecoveryRequiredException(string intentPath)
    : InvalidOperationException($"An interrupted workload operation requires recovery: '{intentPath}'. " +
        "Do not delete the intent or retry mutation until the workload payloads and registry have been reconciled by their owner.")
{
    public string IntentPath { get; } = intentPath;
}

internal sealed class WorkloadPlanChangedException()
    : InvalidOperationException("Installed workload state changed after preparation. Re-run the command to resolve payloads and consent against current state.");

internal sealed class WorkloadLockNotSupportedException(Exception? innerException = null)
    : InvalidOperationException("The workload filesystem does not enforce exclusive file ownership. Use a workload home on a locking-capable local filesystem.",
        innerException);
