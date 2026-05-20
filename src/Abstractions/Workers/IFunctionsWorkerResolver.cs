// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Resolves worker runtimes required by workload project factories.
/// </summary>
public interface IFunctionsWorkerResolver
{
    public Task<FunctionsWorkerResolutionResult> ResolveWorkerAsync(FunctionsWorkerId workerId, CancellationToken cancellationToken);
}
