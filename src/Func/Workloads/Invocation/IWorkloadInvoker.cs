// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Invocation;

/// <summary>
/// Wraps a workload-owned operation so its exceptions are rendered per the
/// workload spec §6.3 invocation contract:
/// <list type="bullet">
///   <item><description><see cref="OperationCanceledException"/> flows through unchanged.</description></item>
///   <item><description>A <see cref="Common.GracefulException"/> from the workload re-emerges with its message
///   prefixed by <c>[&lt;packageId&gt;]</c>, still as a <see cref="Common.GracefulException"/>
///   (user-facing, no stack trace).</description></item>
///   <item><description>Any other exception is wrapped in a <see cref="WorkloadProtocolException"/>
///   with the prefix and a "please file an issue" hint; the original exception is preserved.</description></item>
/// </list>
/// </summary>
internal interface IWorkloadInvoker
{
    /// <summary>Runs <paramref name="operation"/> under the §6.3 contract.</summary>
    public Task InvokeAsync(WorkloadInfo workload, Func<CancellationToken, Task> operation, CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="operation"/> under the §6.3 contract and returns its result.</summary>
    public Task<TResult> InvokeAsync<TResult>(WorkloadInfo workload, Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken);
}
