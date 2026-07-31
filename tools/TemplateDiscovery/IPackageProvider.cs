// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine:
//   repo:   https://github.com/dotnet/templating
//   source: src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/PackChecking/IPackProvider.cs
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Supplies candidate packages to scan, and materialises them to a local <c>.nupkg</c> path on demand.
/// </summary>
internal interface IPackageProvider
{
    public string Name { get; }

    public IAsyncEnumerable<CandidatePackage> GetCandidatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ensures the package is available on disk and returns its local <c>.nupkg</c> path, or
    /// <see langword="null"/> when it could not be obtained.
    /// </summary>
    public Task<string?> EnsureLocalAsync(CandidatePackage package, CancellationToken cancellationToken);

    public void CleanupDownloads();
}
