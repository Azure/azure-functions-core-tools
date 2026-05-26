// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Result of resolving a Functions worker runtime.
/// </summary>
public abstract record FunctionsWorkerResolutionResult
{
    private FunctionsWorkerResolutionResult()
    {
    }

    public sealed record Resolved(IFunctionsWorker Worker) : FunctionsWorkerResolutionResult;

    public sealed record NotResolved(FunctionsWorkerResolutionFailure Failure) : FunctionsWorkerResolutionResult;
}
