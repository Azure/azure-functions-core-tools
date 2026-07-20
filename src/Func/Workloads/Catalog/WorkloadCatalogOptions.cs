// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Configuration for the workload catalog.
/// </summary>
internal sealed class WorkloadCatalogOptions
{
    /// <summary>
    /// Feed location to consult: a v3 <c>index.json</c> URL. <c>null</c>
    /// falls back to nuget.org.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets a value indicating whether catalog operations should include
    /// prerelease versions regardless of per-call arguments.
    /// </summary>
    public bool IncludePrerelease { get; set; }
}
