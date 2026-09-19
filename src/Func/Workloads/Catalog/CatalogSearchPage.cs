// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Workload hits and the number of raw feed entries before client-side filtering.
/// </summary>
internal sealed record CatalogSearchPage(IReadOnlyList<CatalogSearchResult> Items, int RawCount, long? TotalHits = null);