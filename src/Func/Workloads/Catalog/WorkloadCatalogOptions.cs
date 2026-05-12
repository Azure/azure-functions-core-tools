// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Configuration for the workload catalog. Bound from the
/// <c>Workloads:Catalog</c> section; environment variables follow the
/// <c>FUNC_CLI_Workloads__Catalog__Sources__N</c> indexing convention.
/// </summary>
internal sealed class WorkloadCatalogOptions
{
    /// <summary>
    /// Ordered list of feed locations to consult. Each entry is either a
    /// v3 <c>index.json</c> URL or a local directory path. Empty / unset
    /// means fall back to the default nuget.org feed.
    /// </summary>
    public IList<string> Sources { get; set; } = [];
}
