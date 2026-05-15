// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Invocation;

/// <summary>
/// Default <see cref="IWorkloadInvoker"/>. Built-in commands that delegate
/// to a workload (init, new, pack, start, ...) call through here so error
/// rendering stays consistent.
/// </summary>
internal sealed class WorkloadInvoker : IWorkloadInvoker
{
    public async Task InvokeAsync(WorkloadInfo workload, Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GracefulException gex)
        {
            throw RewrapGraceful(workload, gex);
        }
        catch (Exception ex)
        {
            throw WrapProtocol(workload, ex);
        }
    }

    public async Task<TResult> InvokeAsync<TResult>(WorkloadInfo workload, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            return await operation(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GracefulException gex)
        {
            throw RewrapGraceful(workload, gex);
        }
        catch (Exception ex)
        {
            throw WrapProtocol(workload, ex);
        }
    }

    private static GracefulException RewrapGraceful(WorkloadInfo workload, GracefulException original)
        => new(
            message: $"[{workload.PackageId}] {original.Message}",
            isUserError: original.IsUserError,
            verboseMessage: original.VerboseMessage);

    private static WorkloadProtocolException WrapProtocol(WorkloadInfo workload, Exception original)
        => new(
            workload,
            message: $"[{workload.PackageId}] error: {original.Message} " +
                     $"Please file an issue against the workload.",
            innerException: original);
}
