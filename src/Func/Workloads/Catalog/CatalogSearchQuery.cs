// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Query bag for <see cref="IWorkloadCatalog.Search"/>. Pairs with
/// <c>AsyncPageable&lt;CatalogSearchResult&gt;</c>: <see cref="PageSize"/> and
/// <see cref="ContinuationToken"/> follow Azure SDK pagination semantics.
/// </summary>
internal sealed record CatalogSearchQuery
{
    /// <summary>Default page size when <see cref="PageSize"/> is not supplied.</summary>
    public const int DefaultPageSize = 100;

    /// <summary>Free-form search filter, or <c>null</c> for "all matching packages".</summary>
    public string? Filter { get; init; }

    /// <summary>Whether to include prerelease versions in results.</summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Page size requested by the caller. Nullable so a consumer can tell
    /// "use the default" apart from "I asked for N".
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Opaque continuation token from a prior <c>Page&lt;T&gt;</c>. <c>null</c>
    /// starts from the first page.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>Optional <c>--source</c> override; when set, only this source is consulted.</summary>
    public string? OverrideSource { get; init; }
}
