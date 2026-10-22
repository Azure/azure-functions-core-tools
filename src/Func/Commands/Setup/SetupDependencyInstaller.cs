// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Acquisition;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Setup;

internal interface ISetupDependencyInstaller
{
    public Task<SetupDependencyResult> EnsureDependencyAsync(
        SetupCommandOptions options,
        SetupDependency dependency,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resolves a single setup dependency against the workload catalog and installs
/// it when needed, honouring the check and install-policy switches.
/// </summary>
internal sealed class SetupDependencyInstaller(
    IInteractionService interaction,
    IWorkloadStore workloadStore,
    IWorkloadCatalog workloadCatalog,
    IWorkloadInstaller workloadInstaller, NativeTemplateSetupSupport? nativeTemplates = null) : ISetupDependencyInstaller
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));
    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));
    private readonly NativeTemplateSetupSupport? _nativeTemplates = nativeTemplates;

    public async Task<SetupDependencyResult> EnsureDependencyAsync(
        SetupCommandOptions options,
        SetupDependency dependency,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependency);
        if (dependency.TemplateDelivery == SetupTemplateDelivery.ExternalNative)
        {
            return await EnsureNativeAsync(options, dependency, cancellationToken);
        }
        bool requireBundled = _nativeTemplates is not null && dependency.Kind == SetupDependencyKind.Stack;

        IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
        IReadOnlyList<InstalledCandidate> compatibleInstalled = GetInstalledCandidates(installed, dependency, options.IncludePrerelease)
            .Where(candidate => dependency.VersionRange is null || dependency.VersionRange.Satisfies(candidate.Version))
            .OrderByDescending(static candidate => candidate.Version)
            .ToArray();

        if (options.InstallPolicy == SetupInstallPolicy.IfNeeded && compatibleInstalled.Count > 0)
        {
            InstalledCandidate selected = compatibleInstalled[0];
            string? failure = await EnsureBundledAsync(selected.Entry, options.Check, options.Source, dependency.PackageId, cancellationToken, requireBundled);
            if (failure is not null)
            {
                return SetupDependencyResult.Failed(dependency, failure);
            }

            return SetupDependencyResult.Satisfied(
                dependency,
                selected.Entry.PackageId,
                selected.Version.ToNormalizedString(),
                $"{dependency.DisplayName} is already installed.") with { InstalledWorkload = selected.Entry };
        }

        CatalogResolution resolution = await ResolveLatestFromCatalogAsync(dependency, options.Source, options.IncludePrerelease, cancellationToken);
        if (resolution.FailureMessage is not null)
        {
            if (compatibleInstalled.Count > 0)
            {
                InstalledCandidate selected = compatibleInstalled[0];
                string? failure = await EnsureBundledAsync(selected.Entry, options.Check, options.Source, dependency.PackageId, cancellationToken, requireBundled);
                if (failure is not null)
                {
                    return SetupDependencyResult.Failed(dependency, failure);
                }

                return SetupDependencyResult.SatisfiedFallback(
                    dependency,
                    selected.Entry.PackageId,
                    selected.Version.ToNormalizedString(),
                    $"{dependency.DisplayName} is satisfied by installed version {selected.Version.ToNormalizedString()} because catalog resolution failed: {resolution.FailureMessage}")
                    with { InstalledWorkload = selected.Entry };
            }

            if (resolution.PackageMissing && dependency.Optional)
            {
                return SetupDependencyResult.Skipped(
                    dependency,
                    $"Skipped {dependency.DisplayName}: no workload package published for this runtime.");
            }

            return SetupDependencyResult.Failed(dependency, resolution.FailureMessage);
        }

        ResolvedPackage package = resolution.Package!;
        string? channelWarning = resolution.Warning;
        dependency = dependency with { ResolvedPackageId = package.PackageId };

        // Match the exact resolved version directly: the version string already
        // encodes the channel, so this stays correct when a channeled dependency
        // fell back to the stable channel (where the installed version's channel
        // no longer equals dependency.Channel).
        WorkloadEntry? exactInstalled = FindExactInstalled(installed, dependency, package.Version);

        string targetVersion = package.Version.ToNormalizedString();
        if (exactInstalled is not null)
        {
            string? failure = await EnsureBundledAsync(exactInstalled, options.Check, package.Source.Source, package.PackageId, cancellationToken, requireBundled);
            if (failure is not null)
            {
                return SetupDependencyResult.Failed(dependency, failure);
            }

            return SetupDependencyResult.Satisfied(
                dependency,
                package.PackageId,
                targetVersion,
                $"{dependency.DisplayName} {targetVersion} is already installed.")
                with { Warning = channelWarning, InstalledWorkload = exactInstalled };
        }

        if (options.Check)
        {
            return SetupDependencyResult.Failed(
                dependency,
                $"{dependency.DisplayName} {targetVersion} is not installed.") with { Warning = channelWarning };
        }

        try
        {
            WorkloadInstallResult installResult = options.OutputMode == SetupOutputMode.Json
                ? await _workloadInstaller.InstallFromCatalogAsync(
                    package.PackageId,
                    package.Version,
                    options.Source,
                    includePrerelease: options.IncludePrerelease,
                    exact: true,
                    force: false,
                    progress: null,
                    cancellationToken)
                : await _interaction.RunWithProgressAsync(
                    $"Installing {dependency.DisplayName} {targetVersion}",
                    async (ctx, ct) => await _workloadInstaller.InstallFromCatalogAsync(
                        package.PackageId,
                        package.Version,
                        options.Source,
                        includePrerelease: options.IncludePrerelease,
                        exact: true,
                        force: false,
                        new WorkloadInstallProgressAdapter(ctx),
                        ct),
                    cancellationToken);

            string installedVersion = installResult.Entry.PackageVersion;
            if (requireBundled && !_workloadInstaller.HasBundledTemplates(installResult.Entry))
            {
                return SetupDependencyResult.Failed(dependency,
                    "The stack remains installed but has no declared bundled starter assets. A native catalog cannot replace that dependency.")
                    with { PackageId = installResult.Entry.PackageId, Version = installedVersion, InstalledWorkload = installResult.Entry };
            }
            SetupDependencyResult result = installResult.AlreadyInstalled
                ? SetupDependencyResult.Satisfied(
                    dependency,
                    installResult.Entry.PackageId,
                    installedVersion,
                    $"{dependency.DisplayName} {installedVersion} is already installed.") with { Warning = channelWarning }
                : SetupDependencyResult.Installed(
                    dependency,
                    installResult.Entry.PackageId,
                    installedVersion,
                    $"Installed {dependency.DisplayName} {installedVersion}.") with { Warning = channelWarning };
            return result with { InstalledWorkload = installResult.Entry };
        }
        catch (BundledTemplateRegistrationCanceledException ex)
        {
            _interaction.WriteWarning(ex.Message);
            throw;
        }
        catch (WorkloadPackageNotFoundException ex)
        {
            return SetupDependencyResult.Failed(dependency, ex.Message);
        }
        catch (AmbiguousPackageMatchException ex)
        {
            return SetupDependencyResult.Failed(dependency, ex.Message);
        }
        catch (InvalidWorkloadException ex)
        {
            return SetupDependencyResult.Failed(dependency, ex.Message);
        }
        catch (BundledTemplateRegistrationException ex)
        {
            return SetupDependencyResult.Failed(dependency, ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return SetupDependencyResult.Failed(dependency, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return SetupDependencyResult.Failed(dependency, ex.Message);
        }
    }

    private async Task<SetupDependencyResult> EnsureNativeAsync(SetupCommandOptions options, SetupDependency dependency,
        CancellationToken cancellationToken)
    {
        if (_nativeTemplates is null)
        {
            return SetupDependencyResult.Failed(dependency,
                "Required native setup support is unavailable. No optional or versionless legacy templates workload was used.");
        }
        BasicAcquisitionResult result = await _nativeTemplates.EnsureAsync(options, dependency, cancellationToken);
        if (result.Status == BasicAcquisitionStatus.Canceled)
        {
            string completed = result.Completed.Count == 0 ? string.Empty
                : $" Verified completed packages remain: {string.Join(", ", result.Completed.Select(step => $"{step.PackageId}@{step.Version}"))}.";
            _interaction.WriteWarning(result.Message + completed);
            throw new OperationCanceledException(result.Message, result.Cause, cancellationToken);
        }
        BasicAcquisitionStep[] artifacts = [.. result.Completed.Skip(1)];
        if (result.Status != BasicAcquisitionStatus.Completed || artifacts.Length == 0)
        {
            string message = result.Status == BasicAcquisitionStatus.Incomplete
                ? $"Required {dependency.DisplayName} are not installed. Run 'func setup --features {dependency.Name}' without '--check'. Bundled starter availability does not satisfy external templates."
                : $"Required {dependency.DisplayName} were not satisfied ({result.Status}, {result.Failure}). {result.Message}";
            return SetupDependencyResult.Failed(dependency, message) with { NativeAcquisition = result };
        }
        bool installed = artifacts.Any(artifact => !artifact.Reused);
        return new(dependency, installed ? SetupDependencyStatus.Installed : SetupDependencyStatus.Satisfied,
            artifacts.Length == 1 ? artifacts[0].PackageId : null, artifacts.Length == 1 ? artifacts[0].Version : null,
            $"Required {dependency.DisplayName} are verified from their qualified native archives.")
        {
            NativeAcquisition = result,
        };
    }

    private async Task<CatalogResolution> ResolveLatestFromCatalogAsync(
        SetupDependency dependency,
        string? source,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        try
        {
            if (dependency.Channel is { } channel)
            {
                return await ResolveChannelFromCatalogAsync(dependency, channel, source, cancellationToken);
            }

            ResolvedPackage? package = dependency.VersionRange is null
                ? await _workloadCatalog.ResolveLatestVersionAsync(
                    dependency.PackageId,
                    includePrerelease,
                    currentVersion: null,
                    allowMajor: true,
                    source,
                    cancellationToken)
                : await _workloadCatalog.ResolveLatestVersionInRangeAsync(
                    dependency.PackageId,
                    dependency.VersionRange,
                    includePrerelease,
                    source,
                    cancellationToken);

            if (package is null)
            {
                return CatalogResolution.Missing(NoVersionMessage(dependency));
            }

            return CatalogResolution.Resolved(package);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is SetupConfigurationException
            or ArgumentException
            or InvalidOperationException
            or IOException
            or HttpRequestException
            or FatalProtocolException)
        {
            return CatalogResolution.Failed(ex.Message);
        }
    }

    // Resolves a bundle or templates dependency on its declared channel. When the
    // requested channel has nothing published we fall back to the stable channel
    // (with a warning) so setup still provisions a working workload rather than failing.
    private async Task<CatalogResolution> ResolveChannelFromCatalogAsync(
        SetupDependency dependency,
        BundleChannel channel,
        string? source,
        CancellationToken cancellationToken)
    {
        ResolvedPackage? package = await _workloadCatalog.ResolveLatestVersionOnChannelAsync(
            dependency.PackageId,
            channel.ToPrereleaseLabel(),
            dependency.VersionRange,
            source,
            cancellationToken);

        if (package is not null)
        {
            return CatalogResolution.Resolved(package);
        }

        if (channel != BundleChannel.Stable)
        {
            ResolvedPackage? stable = await _workloadCatalog.ResolveLatestVersionOnChannelAsync(
                dependency.PackageId,
                BundleChannel.Stable.ToPrereleaseLabel(),
                dependency.VersionRange,
                source,
                cancellationToken);

            if (stable is not null)
            {
                return CatalogResolution.Resolved(stable, ChannelFallbackWarning(dependency, channel));
            }
        }

        return CatalogResolution.Missing(NoVersionMessage(dependency));
    }

    private static string ChannelFallbackWarning(SetupDependency dependency, BundleChannel channel)
    {
        string suggestion = dependency.SearchAlias is { } alias
            ? $" Find a matching workload with: func workload search {alias} --prerelease"
            : string.Empty;
        return $"No '{channel.ToDisplayString()}' {dependency.DisplayName} is available; using stable instead.{suggestion}";
    }

    private static string NoVersionMessage(SetupDependency dependency)
    {
        string range = dependency.RangeText is null ? string.Empty : $" in range '{dependency.RangeText}'";
        return $"No {dependency.DisplayName} workload version{range} is available from the configured workload catalog.";
    }

    private static IReadOnlyList<InstalledCandidate> GetInstalledCandidates(
        IReadOnlyList<WorkloadEntry> installed,
        SetupDependency dependency,
        bool includePrerelease)
    {
        List<InstalledCandidate> candidates = [];
        foreach (WorkloadEntry entry in installed)
        {
            if (!MatchesDependency(entry, dependency)
                || !NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? version))
            {
                continue;
            }

            // A channeled dependency only counts an installed version that belongs
            // to the same channel; otherwise fall back to the prerelease toggle.
            if (dependency.Channel is { } channel)
            {
                if (!BundleHelpers.MatchesChannel(version, channel))
                {
                    continue;
                }
            }
            else if (!includePrerelease && version.IsPrerelease)
            {
                continue;
            }

            candidates.Add(new InstalledCandidate(entry, version));
        }

        return candidates;
    }

    private async Task<string?> EnsureBundledAsync(WorkloadEntry entry, bool checkOnly, string? requestedSource,
        string requestedPackageId, CancellationToken cancellationToken, bool required = false)
    {
        if (!entry.BundledTemplatesDeclared)
        {
            if (!required)
            {
                return null;
            }
            if (!_workloadInstaller.HasBundledTemplates(entry))
            {
                return "The selected stack has no declared bundled starter assets. Required external native templates cannot replace bundled readiness.";
            }
        }

        string actualSource = entry.LogicalPackage is { } logical
            && string.Equals(requestedPackageId, logical.PackageId, StringComparison.OrdinalIgnoreCase) ? logical.Source : entry.Source;
        if (requestedSource is not null && !string.Equals(requestedSource, actualSource, StringComparison.Ordinal))
        {
            return "The requested source conflicts with the installed bundled stack's validated origin. Use its original source to repair it.";
        }

        try
        {
            await _workloadInstaller.EnsureInstalledAsync(entry, cancellationToken, checkOnly);
            return null;
        }
        catch (BundledTemplateRegistrationCanceledException exception)
        {
            _interaction.WriteWarning(exception.Message);
            throw;
        }
        catch (BundledTemplateRegistrationException exception)
        {
            return exception.Message;
        }
        catch (InvalidWorkloadException exception)
        {
            return exception.Message;
        }
    }

    private static WorkloadEntry? FindExactInstalled(IReadOnlyList<WorkloadEntry> installed, SetupDependency dependency, NuGetVersion version)
    {
        foreach (WorkloadEntry entry in installed)
        {
            if (MatchesDependency(entry, dependency)
                && NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? installedVersion)
                && installedVersion.Equals(version))
            {
                return entry;
            }
        }

        return null;
    }

    private static bool MatchesDependency(WorkloadEntry entry, SetupDependency dependency)
    {
        if (dependency.ResolvedPackageId is { } resolvedPackageId
            && MatchesPackageId(entry, resolvedPackageId))
        {
            return true;
        }

        return MatchesPackageId(entry, dependency.PackageId);
    }

    private static bool MatchesPackageId(WorkloadEntry entry, string packageId)
        => string.Equals(entry.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
            || (entry.LogicalPackage is { } logical
                && string.Equals(logical.PackageId, packageId, StringComparison.OrdinalIgnoreCase));

    private sealed record CatalogResolution(ResolvedPackage? Package, string? FailureMessage, bool PackageMissing, string? Warning = null)
    {
        public static CatalogResolution Resolved(ResolvedPackage package, string? warning = null) => new(package, FailureMessage: null, PackageMissing: false, warning);

        public static CatalogResolution Failed(string failureMessage) => new(Package: null, failureMessage, PackageMissing: false);

        public static CatalogResolution Missing(string failureMessage) => new(Package: null, failureMessage, PackageMissing: true);
    }

    private sealed record InstalledCandidate(WorkloadEntry Entry, NuGetVersion Version);
}
