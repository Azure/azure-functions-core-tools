// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Creates default worker resolvers with profile constraints applied.
/// </summary>
internal sealed class DefaultFunctionsWorkerResolverFactory(
    IWorkloadProvider workloadProvider,
    IWorkerConfigFileSystem workerConfigFileSystem) : IFunctionsWorkerResolverFactory
{
    private readonly IWorkloadProvider _workloadProvider = workloadProvider ?? throw new ArgumentNullException(nameof(workloadProvider));
    private readonly IWorkerConfigFileSystem _workerConfigFileSystem = workerConfigFileSystem
        ?? throw new ArgumentNullException(nameof(workerConfigFileSystem));

    public IFunctionsWorkerResolver Create(IReadOnlyDictionary<string, VersionRange> workerVersionRanges)
    {
        ArgumentNullException.ThrowIfNull(workerVersionRanges);
        return new DefaultFunctionsWorkerResolver(_workloadProvider, _workerConfigFileSystem, workerVersionRanges);
    }
}
