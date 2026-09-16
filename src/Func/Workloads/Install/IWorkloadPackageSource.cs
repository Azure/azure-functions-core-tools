// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal interface IWorkloadPackageSource
{
    public Task<ResolvedPackage> ResolveAsync(string packageId, NuGetVersion? version, string? source,
        bool? includePrerelease, bool exact, CancellationToken cancellationToken = default);

    public Task<ResolvedPackage?> ResolveLatestVersionAsync(string packageId, bool? includePrerelease, NuGetVersion currentVersion,
        bool allowMajor, string? source, CancellationToken cancellationToken = default);

    public Task<ResolvedPackage> ResolveImplementationAsync(InspectedWorkloadPackage pointer,
        WorkloadPointerSelection selection, string source, CancellationToken cancellationToken = default);

    public string FindLocalImplementation(string pointerPath, string packageId, string version, string runtimeIdentifier);

    public Task<TemporaryWorkloadPackageFile> DownloadAsync(ResolvedPackage package, IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default);

    public bool IsLocal(string source);
}
