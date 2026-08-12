// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal interface IWorkloadDeploymentService
{
    public Task<WorkloadEntry> GetUpdateTargetAsync(string packageId, NuGetVersion? targetInstalledVersion,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken = default);

    public Task<WorkloadInstallResult?> TryReuseExplicitAsync(string packageId, string version, CancellationToken cancellationToken = default);

    public Task<WorkloadInstallResult?> TryReuseImplementationAsync(string packageId, string version, string runtimeIdentifier,
        LogicalPackage logicalPackage, CancellationToken cancellationToken = default);

    public Task<WorkloadInstallResult?> TryReuseImplementationForUpdateAsync(WorkloadEntry currentEntry, string packageId, string version,
        string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken = default);

    public Task<WorkloadInstallResult> InstallAsync(InspectedWorkloadPackage package, string source, WorkloadOwnershipKind ownership,
        LogicalPackage? logicalPackage, bool force, IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default);

    public Task<WorkloadEntry> UpdateAsync(WorkloadEntry currentEntry, InspectedWorkloadPackage package, string source,
        WorkloadOwnershipKind ownership, LogicalPackage? logicalPackage, IProgress<WorkloadInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    public Task<bool> UninstallAsync(string packageId, string version, WorkloadOwnershipKind ownership, CancellationToken cancellationToken = default);
}
