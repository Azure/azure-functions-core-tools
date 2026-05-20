// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Creates <see cref="FunctionsWorkerResolutionResult"/> instances.
/// </summary>
public static class FunctionsWorkerResolutionResults
{
    public static FunctionsWorkerResolutionResult Resolved(IFunctionsWorker worker)
        => new FunctionsWorkerResolutionResult.Resolved(worker);

    public static FunctionsWorkerResolutionResult NotResolved(FunctionsWorkerResolutionFailure failure)
        => new FunctionsWorkerResolutionResult.NotResolved(failure);
}
