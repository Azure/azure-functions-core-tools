// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Resolves the ordered list of package sources the workload catalog consults.
/// </summary>
internal interface IPackageSourceProvider
{
    /// <summary>
    /// Returns the sources to consult. When <paramref name="overrideSource"/>
    /// is non-null and non-empty, the result contains exactly that single
    /// source.
    /// </summary>
    /// <param name="overrideSource">
    /// Optional explicit source from <c>--source</c>: a v3 <c>index.json</c>
    /// URL or a local directory path.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="overrideSource"/> is not a recognisable URL or
    /// existing directory; or a configured source entry is invalid.
    /// </exception>
    public IReadOnlyList<PackageSource> GetSources(string? overrideSource = null);
}
