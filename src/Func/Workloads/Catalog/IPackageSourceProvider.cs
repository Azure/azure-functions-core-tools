// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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
    /// configured <c>Workloads:Catalog:Source</c>, then the nuget.org default.
    /// </summary>
    /// <param name="source">
    /// Optional explicit source: a v3 <c>index.json</c> URL or a local
    /// directory path. <c>null</c> means "use the configured / default source".
    /// </param>
    /// <exception cref="ArgumentException">
    /// The resolved source is not a recognisable URL or existing directory.
    /// </exception>
    public PackageSource GetSource(string? source = null);
}
