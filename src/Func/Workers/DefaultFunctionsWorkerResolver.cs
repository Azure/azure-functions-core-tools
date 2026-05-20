// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Default worker resolver until worker workload discovery is implemented.
/// </summary>
internal sealed class DefaultFunctionsWorkerResolver : IFunctionsWorkerResolver
{
    public Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(FunctionsWorkerId workerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        cancellationToken.ThrowIfCancellationRequested();

        FunctionsWorkerResolutionFailure failure = FunctionsWorkerResolutionFailures.NotInstalled(
            workerId,
            $"No installed Azure Functions worker was found for '{workerId.Value}'.");

        return Task.FromResult<FunctionsWorkerResolutionResult>(
            FunctionsWorkerResolutionResults.NotResolved(failure));
    }
}
