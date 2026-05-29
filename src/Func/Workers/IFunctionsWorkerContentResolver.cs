// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Resolves a worker from installed content workload metadata.
/// </summary>
internal interface IFunctionsWorkerContentResolver
{
    public FunctionsWorkerResolutionResult ResolveWorker(
        FunctionsWorkerId workerId,
        IReadOnlyList<ContentWorkloadInfo> installedWorkers,
        VersionRange? versionConstraint,
        CancellationToken cancellationToken);
}
