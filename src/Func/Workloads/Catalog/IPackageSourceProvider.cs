// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using PackageSource = NuGet.Configuration.PackageSource;

namespace Azure.Functions.Cli.Workloads.Catalog;

/// <summary>
/// Resolves the single <see cref="PackageSource"/> the workload catalog consults.
/// </summary>
internal interface IPackageSourceProvider
{
    /// <summary>
    /// Returns the source to consult. <paramref name="source"/> takes precedence
    /// (typically from <c>--source</c>); <c>null</c> falls through to the
    /// <see cref="Constants.WorkloadsSourceEnvironmentVariable"/> env var,
    /// then the nuget.org default.
    /// </summary>
    /// <param name="source">
    /// Optional explicit source: an absolute HTTP(S) URL for a V3 service index.
    /// <c>null</c> means "use the configured / default source".
    /// </param>
    /// <exception cref="ArgumentException">
    /// The resolved source is not an absolute HTTP(S) URL. Local paths are not supported.
    /// </exception>
    public PackageSource GetSource(string? source = null);
}
