// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Query bag for <see cref="IWorkloadCatalog.SearchAsync"/>.
/// </summary>
internal sealed record CatalogSearchQuery
{
    /// <summary>Default page size when <see cref="Take"/> is not supplied.</summary>
    public const int DefaultTake = 100;

    /// <summary>Free-form search filter, or <c>null</c> for "all matching packages".</summary>
    public string? Filter { get; init; }

    /// <summary>Whether to include prerelease versions in results.</summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>Number of results to skip; defaults to 0.</summary>
    public int Skip { get; init; }

    /// <summary>Maximum number of results to return; defaults to <see cref="DefaultTake"/>.</summary>
    public int? Take { get; init; }

    /// <summary>Optional <c>--source</c> override; <c>null</c> uses the configured / default source.</summary>
    public string? Source { get; init; }
}
