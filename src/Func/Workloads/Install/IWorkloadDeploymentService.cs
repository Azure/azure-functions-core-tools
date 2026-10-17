// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal interface IWorkloadDeploymentService
{
    public bool HasBundledTemplates(WorkloadEntry entry);
    public Task<PreparedWorkloadReuse> PrepareReuseAsync(WorkloadEntry entry, CancellationToken cancellationToken);
    /// <summary>
    /// Revalidates the leased registry and payload, optionally checking native registration without registering packages.
    /// </summary>
    public Task ValidateReuseAsync(IWorkloadOperation operation, PreparedWorkloadReuse prepared, CancellationToken cancellationToken, bool checkOnly = false);
    /// <summary>
    /// Revalidates reuse and admits workload ownership before registering bundles under continuous native protection.
    /// </summary>
    public Task<WorkloadInstallResult> CommitPreparedReuseAsync(IWorkloadOperation operation, PreparedWorkloadReuse prepared,
        Func<CancellationToken, Task<WorkloadInstallResult>> admitWorkload, CancellationToken cancellationToken);
    public Task<WorkloadEntry> GetAdvisoryUpdateTargetAsync(string packageId, NuGetVersion? targetInstalledVersion,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken);
    public Task<WorkloadInstallResult?> GetAdvisoryExplicitReuseAsync(string packageId, string version, CancellationToken cancellationToken);
    public Task<WorkloadEntry> GetUpdateTargetAsync(IWorkloadOperation operation, string packageId, NuGetVersion? targetInstalledVersion,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken);
    public Task<PreparedWorkloadDeployment> PrepareAsync(InspectedWorkloadPackage package, string source,
        WorkloadOwnershipKind ownership, LogicalPackage? logicalPackage, bool force,
        IProgress<WorkloadInstallProgress>? progress, CancellationToken cancellationToken, WorkloadEntry? updateFrom = null);
    public Task<WorkloadInstallResult> InstallPreparedAsync(IWorkloadOperation operation, PreparedWorkloadDeployment prepared,
        IProgress<WorkloadInstallProgress>? progress, CancellationToken cancellationToken);
    public Task<WorkloadEntry> UpdatePreparedAsync(IWorkloadOperation operation, WorkloadEntry currentEntry,
        PreparedWorkloadDeployment prepared, CancellationToken cancellationToken);
    public Task<WorkloadEntry?> GetReusableImplementationAsync(string packageId, string version, string runtimeIdentifier, CancellationToken cancellationToken);
    public Task<WorkloadInstallResult?> TryReuseExplicitAsync(IWorkloadOperation operation, string packageId, string version,
        CancellationToken cancellationToken);
    public Task<WorkloadInstallResult?> TryReuseImplementationAsync(IWorkloadOperation operation, string packageId, string version,
        string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken);
    public Task<WorkloadInstallResult?> TryReuseImplementationForUpdateAsync(IWorkloadOperation operation, WorkloadEntry currentEntry,
        string packageId, string version, string runtimeIdentifier, LogicalPackage logicalPackage, CancellationToken cancellationToken);
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
    public Task<bool> UninstallAsync(IWorkloadOperation operation, string packageId, string version,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken);
}
