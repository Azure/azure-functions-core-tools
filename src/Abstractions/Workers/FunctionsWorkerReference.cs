// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Describes how a Functions project resolves its required worker.
/// </summary>
public abstract class FunctionsWorkerReference
{
    private FunctionsWorkerReference()
    {
    }

    public static FunctionsWorkerReference FromWorkload(string workerId)
        => FromWorkload(new FunctionsWorkerId(workerId));

    public static FunctionsWorkerReference FromWorkload(FunctionsWorkerId workerId)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        return new WorkloadReference(workerId);
    }

    public static FunctionsWorkerReference FromWorkerInfo(string workerId, string workerRuntime, string workerConfigPath, string version = "")
        => FromWorkerInfo(new FunctionsWorkerId(workerId), workerRuntime, workerConfigPath, version);

    public static FunctionsWorkerReference FromWorkerInfo(FunctionsWorkerId workerId, string workerRuntime, string workerConfigPath, string version = "")
    {
        ArgumentNullException.ThrowIfNull(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerConfigPath);
        ArgumentNullException.ThrowIfNull(version);

        IFunctionsWorker worker = new ReferencedFunctionsWorker(workerId, workerRuntime, workerConfigPath, version);
        return new WorkerInfoReference(worker);
    }

    public abstract Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(FunctionsWorkerResolutionContext context, CancellationToken cancellationToken);

    private sealed class WorkloadReference(FunctionsWorkerId workerId) : FunctionsWorkerReference
    {
        private readonly FunctionsWorkerId _workerId = workerId ?? throw new ArgumentNullException(nameof(workerId));

        public override async Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(FunctionsWorkerResolutionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            return await context.Resolver.ResolveWorkerAsync(_workerId, cancellationToken);
        }
    }

    private sealed class WorkerInfoReference(IFunctionsWorker worker) : FunctionsWorkerReference
    {
        private readonly IFunctionsWorker _worker = worker ?? throw new ArgumentNullException(nameof(worker));

        public override Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(FunctionsWorkerResolutionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<FunctionsWorkerResolutionResult>(FunctionsWorkerResolutionResults.Resolved(_worker));
        }
    }

    private sealed record ReferencedFunctionsWorker(FunctionsWorkerId Id, string WorkerRuntime, string WorkerConfigPath, string Version) : IFunctionsWorker;
}
