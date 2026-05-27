// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Configuration for the workload catalog. The only supported override for
/// <see cref="Source"/> is the
/// <see cref="Constants.WorkloadsSourceEnvironmentVariable"/> environment
/// variable, read inside the default constructor via
/// <see cref="WorkloadSourceResolver.Resolve"/>. Other configuration sources
/// (json files, local.settings, in-memory) are not honored.
/// </summary>
internal sealed class WorkloadCatalogOptions
{
    /// <summary>
    /// Resolves <see cref="Source"/> from
    /// <see cref="Constants.WorkloadsSourceEnvironmentVariable"/>.
    /// </summary>
    public WorkloadCatalogOptions()
        : this(WorkloadSourceResolver.Resolve())
    {
    }

    /// <summary>
    /// Test-only seam for supplying <see cref="Source"/> directly so unit
    /// tests don't have to mutate process-global env vars.
    /// </summary>
    internal WorkloadCatalogOptions(string? source)
    {
        Source = source;
    }

    /// <summary>
    /// Feed location to consult: a v3 <c>index.json</c> URL. <c>null</c>
    /// falls back to nuget.org.
    /// </summary>
    public string? Source { get; }
}
