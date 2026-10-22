// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Workloads.Install;

internal sealed class WorkloadInstaller(IWorkloadPackageSource packageSource, IWorkloadPackageInspector packageInspector,
    WorkloadRidPackageSelector ridPackageSelector, IWorkloadDeploymentService deploymentService, IWorkloadPackageTrust packageTrust) : IWorkloadInstaller
{
    private readonly IWorkloadPackageSource _packageSource =
        packageSource ?? throw new ArgumentNullException(nameof(packageSource));
    private readonly IWorkloadPackageInspector _packageInspector =
        packageInspector ?? throw new ArgumentNullException(nameof(packageInspector));
    private readonly WorkloadRidPackageSelector _ridPackageSelector =
        ridPackageSelector ?? throw new ArgumentNullException(nameof(ridPackageSelector));
    private readonly IWorkloadDeploymentService _deploymentService =
        deploymentService ?? throw new ArgumentNullException(nameof(deploymentService));
    private readonly IWorkloadPackageTrust _packageTrust =
        packageTrust ?? throw new ArgumentNullException(nameof(packageTrust));

    public async Task<WorkloadInstallResult> InstallFromPackageAsync(string nupkgPath, bool force = false,
        IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default, bool allowUntrusted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);

        if (!File.Exists(nupkgPath))
        {
            throw new FileNotFoundException($"Package file '{nupkgPath}' does not exist.", nupkgPath);
        }

        string fullPath = Path.GetFullPath(nupkgPath);
        using WorkloadPackageSnapshot snapshot = await _packageTrust.AcquireAsync(fullPath, allowUntrusted, cancellationToken);
        InspectedWorkloadPackage package = await _packageInspector.InspectAsync(snapshot.Path, cancellationToken);

        if (package.Role != WorkloadPackageRole.Pointer)
        {
            return await _deploymentService.InstallAsync(
                package, fullPath, WorkloadOwnershipKind.Explicit, logicalPackage: null, force, progress, cancellationToken);
        }

        WorkloadPointerSelection selection = _ridPackageSelector.SelectImplementation(package);
        LogicalPackage logicalPackage = CreateLogicalPackage(package, fullPath);
        string implementationPath = _packageSource.FindLocalImplementation(fullPath, selection.PackageId, package.Identity.Version, selection.RuntimeIdentifier);
        using WorkloadPackageSnapshot implementationSnapshot = await _packageTrust.AcquireAsync(implementationPath, allowUntrusted, cancellationToken);
        InspectedWorkloadPackage implementation = await _packageInspector.InspectAsync(implementationSnapshot.Path, cancellationToken);
        _ridPackageSelector.ValidateImplementation(package, selection, implementation);

        return await _deploymentService.InstallAsync(implementation, implementationPath, WorkloadOwnershipKind.Logical, logicalPackage, force, progress, cancellationToken);
    }

    public async Task<WorkloadInstallResult> InstallFromCatalogAsync(string packageId, NuGetVersion? version, string? source,
        bool? includePrerelease, bool exact, bool force, IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default,
        bool allowUntrusted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        progress?.Report(new WorkloadInstallProgress(
            WorkloadInstallPhase.Resolving, $"Resolving workload '{packageId}'"));

        ResolvedPackage resolved = await _packageSource.ResolveAsync(packageId, version, source, includePrerelease, exact, cancellationToken);
        WorkloadInstallResult? reused = force
            ? null
            : await _deploymentService.TryReuseExplicitAsync(resolved.PackageId, resolved.Version.ToNormalizedString(), cancellationToken);

        if (reused is not null)
        {
            return reused;
        }

        using TemporaryWorkloadPackageFile packageDownload = await _packageSource.DownloadAsync(resolved, progress, cancellationToken);
        using WorkloadPackageSnapshot snapshot = await _packageTrust.AcquireAsync(packageDownload.Path, allowUntrusted, cancellationToken);
        InspectedWorkloadPackage package = await _packageInspector.InspectAsync(snapshot.Path, cancellationToken);
        ValidateResolvedIdentity(resolved, package.Identity);

        if (package.Role != WorkloadPackageRole.Pointer)
        {
            return await _deploymentService.InstallAsync(
                package, resolved.Source.Source, WorkloadOwnershipKind.Explicit, logicalPackage: null, force, progress, cancellationToken);
        }

        WorkloadPointerSelection selection = _ridPackageSelector.SelectImplementation(package);
        LogicalPackage logicalPackage = CreateLogicalPackage(package, resolved.Source.Source);
        WorkloadInstallResult? existingImplementation = force
            ? null
            : await _deploymentService.TryReuseImplementationAsync(
                selection.PackageId, package.Identity.Version, selection.RuntimeIdentifier, logicalPackage, cancellationToken);

        if (existingImplementation is not null)
        {
            return existingImplementation;
        }

        ResolvedPackage implementationResolved = await _packageSource.ResolveImplementationAsync(package, selection, resolved.Source.Source, cancellationToken);
        using TemporaryWorkloadPackageFile implementationDownload = await _packageSource.DownloadAsync(implementationResolved, progress, cancellationToken);
        using WorkloadPackageSnapshot implementationSnapshot = await _packageTrust.AcquireAsync(implementationDownload.Path, allowUntrusted, cancellationToken);
        InspectedWorkloadPackage implementation = await _packageInspector.InspectAsync(implementationSnapshot.Path, cancellationToken);

        ValidateResolvedIdentity(implementationResolved, implementation.Identity);

        _ridPackageSelector.ValidateImplementation(package, selection, implementation);

        return await _deploymentService.InstallAsync(implementation, implementationResolved.Source.Source, WorkloadOwnershipKind.Logical, logicalPackage, force, progress, cancellationToken);
    }

    public async Task<WorkloadUpdateResult> UpdateAsync(string packageId, NuGetVersion? targetInstalledVersion, string? source,
        bool? includePrerelease, bool allowMajor, IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default,
        bool allowUntrusted = false)
        => await UpdateAsync(packageId, targetInstalledVersion, source, includePrerelease, allowMajor, WorkloadOwnershipKind.Explicit, progress, cancellationToken, allowUntrusted);

    public async Task<WorkloadUpdateResult> UpdateAsync(string packageId, NuGetVersion? targetInstalledVersion, string? source,
        bool? includePrerelease, bool allowMajor, WorkloadOwnershipKind ownership,
        IProgress<WorkloadInstallProgress>? progress = null, CancellationToken cancellationToken = default, bool allowUntrusted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return ownership == WorkloadOwnershipKind.Logical
            ? await UpdateLogicalAsync(
                packageId, targetInstalledVersion, source, includePrerelease, allowMajor, progress, cancellationToken, allowUntrusted)
            : await UpdateExplicitAsync(
                packageId, targetInstalledVersion, source, includePrerelease, allowMajor, progress, cancellationToken, allowUntrusted);
    }

    public async Task<bool> UninstallAsync(string packageId, string version, CancellationToken cancellationToken = default)
        => await UninstallAsync(packageId, version, WorkloadOwnershipKind.Explicit, cancellationToken);

    public async Task<bool> UninstallAsync(string packageId, string version,
        WorkloadOwnershipKind ownership, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        return await _deploymentService.UninstallAsync(packageId, version, ownership, cancellationToken);
    }

    private async Task<WorkloadUpdateResult> UpdateExplicitAsync(string packageId, NuGetVersion? targetInstalledVersion, string? source,
        bool? includePrerelease, bool allowMajor, IProgress<WorkloadInstallProgress>? progress, CancellationToken cancellationToken, bool allowUntrusted)
    {
        WorkloadEntry currentEntry = await _deploymentService.GetUpdateTargetAsync(packageId, targetInstalledVersion, WorkloadOwnershipKind.Explicit, cancellationToken);
        var currentVersion = NuGetVersion.Parse(currentEntry.PackageVersion);
        ResolvedPackage? resolved = await _packageSource.ResolveLatestVersionAsync(currentEntry.PackageId, includePrerelease, currentVersion, allowMajor, source, cancellationToken);

        if (resolved is null)
        {
            return new WorkloadUpdateResult(currentEntry, currentEntry.PackageVersion, true, true);
        }

        if (resolved.Version <= currentVersion)
        {
            return new WorkloadUpdateResult(currentEntry, currentEntry.PackageVersion, true);
        }

        using TemporaryWorkloadPackageFile download = await _packageSource.DownloadAsync(resolved, progress, cancellationToken);
        using WorkloadPackageSnapshot snapshot = await _packageTrust.AcquireAsync(download.Path, allowUntrusted, cancellationToken);
        InspectedWorkloadPackage package = await _packageInspector.InspectAsync(snapshot.Path, cancellationToken);
        ValidateResolvedIdentity(resolved, package.Identity);
        WorkloadEntry newEntry = await _deploymentService.UpdateAsync(currentEntry, package, resolved.Source.Source, WorkloadOwnershipKind.Explicit, logicalPackage: null, progress, cancellationToken);

        return new WorkloadUpdateResult(newEntry, currentEntry.PackageVersion, false);
    }

    private async Task<WorkloadUpdateResult> UpdateLogicalAsync(string packageId, NuGetVersion? targetInstalledVersion, string? source,
        bool? includePrerelease, bool allowMajor, IProgress<WorkloadInstallProgress>? progress, CancellationToken cancellationToken, bool allowUntrusted)
    {
        WorkloadEntry currentEntry = await _deploymentService.GetUpdateTargetAsync(packageId, targetInstalledVersion, WorkloadOwnershipKind.Logical, cancellationToken);
        LogicalPackage currentLogical = currentEntry.LogicalPackage!;

        if (source is null && _packageSource.IsLocal(currentLogical.Source))
        {
            throw new InvalidOperationException(
                $"Logical workload '{currentLogical.PackageId}' was installed from a local package and has no automatic update source. " +
                "Install a newer local pointer package explicitly or pass --source.");
        }

        var currentVersion = NuGetVersion.Parse(currentLogical.PackageVersion);
        string? effectiveSource = source
            ?? (string.IsNullOrWhiteSpace(currentLogical.Source) ? null : currentLogical.Source);
        ResolvedPackage? pointerResolved = await _packageSource.ResolveLatestVersionAsync(currentLogical.PackageId, includePrerelease, currentVersion, allowMajor, effectiveSource, cancellationToken);

        if (pointerResolved is null)
        {
            return new WorkloadUpdateResult(currentEntry, currentLogical.PackageVersion, true, true);
        }

        if (pointerResolved.Version < currentVersion)
        {
            return new WorkloadUpdateResult(currentEntry, currentLogical.PackageVersion, true);
        }

        using TemporaryWorkloadPackageFile pointerDownload = await _packageSource.DownloadAsync(pointerResolved, progress, cancellationToken);
        using WorkloadPackageSnapshot pointerSnapshot = await _packageTrust.AcquireAsync(pointerDownload.Path, allowUntrusted, cancellationToken);
        InspectedWorkloadPackage pointer = await _packageInspector.InspectAsync(pointerSnapshot.Path, cancellationToken);

        ValidateResolvedIdentity(pointerResolved, pointer.Identity);

        if (pointer.Role != WorkloadPackageRole.Pointer)
        {
            throw new InvalidWorkloadException(
                $"Package '{pointer.Identity.PackageId}' {pointer.Identity.Version} is not a rid-pointer workload.");
        }

        WorkloadPointerSelection selection = _ridPackageSelector.SelectImplementation(pointer);
        bool ridPivot = !string.Equals(
            currentEntry.RuntimeIdentifier, selection.RuntimeIdentifier, StringComparison.Ordinal);

        if (pointerResolved.Version == currentVersion && !ridPivot)
        {
            return new WorkloadUpdateResult(currentEntry, currentLogical.PackageVersion, true);
        }

        LogicalPackage newLogical = CreateLogicalPackage(pointer, pointerResolved.Source.Source);
        WorkloadInstallResult? reusable = await _deploymentService.TryReuseImplementationForUpdateAsync(
            currentEntry, selection.PackageId, pointer.Identity.Version, selection.RuntimeIdentifier, newLogical, cancellationToken);

        if (reusable is not null)
        {
            return new WorkloadUpdateResult(reusable.Entry, currentLogical.PackageVersion, false);
        }

        ResolvedPackage implementationResolved = await _packageSource.ResolveImplementationAsync(pointer, selection, pointerResolved.Source.Source, cancellationToken);
        using TemporaryWorkloadPackageFile implementationDownload = await _packageSource.DownloadAsync(implementationResolved, progress, cancellationToken);
        using WorkloadPackageSnapshot implementationSnapshot = await _packageTrust.AcquireAsync(implementationDownload.Path, allowUntrusted, cancellationToken);
        InspectedWorkloadPackage implementation = await _packageInspector.InspectAsync(implementationSnapshot.Path, cancellationToken);
        ValidateResolvedIdentity(implementationResolved, implementation.Identity);
        _ridPackageSelector.ValidateImplementation(pointer, selection, implementation);
        WorkloadEntry newEntry = await _deploymentService.UpdateAsync(currentEntry, implementation, implementationResolved.Source.Source, WorkloadOwnershipKind.Logical, newLogical, progress, cancellationToken);

        return new WorkloadUpdateResult(newEntry, currentLogical.PackageVersion, false);
    }

    private static LogicalPackage CreateLogicalPackage(InspectedWorkloadPackage pointer, string source)
        => new()
        {
            PackageId = pointer.Identity.PackageId,
            PackageVersion = pointer.Identity.Version,
            Aliases = pointer.Identity.Aliases,
            DisplayName = GetDisplayName(pointer.Metadata, pointer.Identity.Title, pointer.Identity.PackageId),
            Description = GetDescription(pointer.Metadata, pointer.Identity.Description),
            Source = source,
        };

    private void ValidateResolvedIdentity(ResolvedPackage resolved, WorkloadPackageIdentity identity)
        => _packageInspector.ValidateIdentity(identity, resolved.PackageId, resolved.Version.ToNormalizedString());

    private static string GetDisplayName(WorkloadMetadata metadata, string? nuspecTitle, string packageId)
        => !string.IsNullOrWhiteSpace(metadata.DisplayName)
            ? metadata.DisplayName
            : string.IsNullOrWhiteSpace(nuspecTitle) ? packageId : nuspecTitle;

    private static string GetDescription(WorkloadMetadata metadata, string? nuspecDescription)
        => !string.IsNullOrWhiteSpace(metadata.Description)
            ? metadata.Description
            : string.IsNullOrWhiteSpace(nuspecDescription) ? string.Empty : nuspecDescription;
}
