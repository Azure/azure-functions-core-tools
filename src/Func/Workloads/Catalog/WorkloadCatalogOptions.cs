// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Configuration for the workload catalog. Bound from the
/// <c>Workloads:Catalog</c> section; the matching environment variable is
/// <c>FUNC_CLI_Workloads__Catalog__Source</c>.
/// </summary>
internal sealed class WorkloadCatalogOptions
{
    /// <summary>
    /// Feed location to consult: either a v3 <c>index.json</c> URL or a
    /// local directory path. <c>null</c> / empty falls back to nuget.org.
    /// </summary>
    public string? Source { get; set; }
}
