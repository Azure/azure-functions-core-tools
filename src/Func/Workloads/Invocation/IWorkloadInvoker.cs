// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Invocation;

/// <summary>
/// Wraps a workload-owned operation so unexpected exceptions become
/// <see cref="WorkloadProtocolException"/>s and any
/// <see cref="Common.GracefulException"/> the workload throws re-emerges
/// with a <c>[&lt;packageId&gt;]</c> prefix.
/// </summary>
internal interface IWorkloadInvoker
{
    public Task InvokeAsync(WorkloadInfo workload, Func<CancellationToken, Task> operation, CancellationToken cancellationToken);

    public Task<TResult> InvokeAsync<TResult>(WorkloadInfo workload, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken);
}
