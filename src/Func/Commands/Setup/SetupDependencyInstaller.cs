// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
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
    IWorkloadInstaller workloadInstaller,
    IWorkloadPaths workloadPaths,
    IFileSystem fileSystem) : ISetupDependencyInstaller
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));
    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));
    private readonly IWorkloadPaths _workloadPaths = workloadPaths ?? throw new ArgumentNullException(nameof(workloadPaths));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<SetupDependencyResult> EnsureDependencyAsync(
        SetupCommandOptions options,
        SetupDependency dependency,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependency);

        IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
        if (dependency.Kind == SetupDependencyKind.Templates && dependency.StackSelection is { } selection)
        {
            var stack = SetupDependency.Stack(dependency.Name);
            WorkloadEntry? selected = installed.SingleOrDefault(entry =>
                string.Equals(entry.PackageId, selection.PackageId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.PackageVersion, selection.Version, StringComparison.Ordinal));
            if (selected is null)
            {
                return SetupDependencyResult.Failed(dependency, "The selected stack changed during setup. Run setup again before checking its templates.");
            }

            if (selected.BundledTemplatesDeclared || _workloadInstaller.HasBundledTemplates(selected))
            {
                string? failure = await EnsureRetainedDependencyAsync(selected, options.Check, options.Source, stack.PackageId, cancellationToken);
                return failure is null
                    ? SetupDependencyResult.Satisfied(dependency, selected.PackageId, selected.PackageVersion,
                        $"{dependency.DisplayName} are bundled with the installed {dependency.Name} stack.")
                    : SetupDependencyResult.Failed(dependency, failure);
            }
        }

        IReadOnlyList<InstalledCandidate> compatibleInstalled = GetInstalledCandidates(installed, dependency, options.IncludePrerelease)
            .Where(candidate => dependency.VersionRange is null
                || WorkloadVersionRanges.SatisfiesRange(dependency.VersionRange, candidate.Version, options.IncludePrerelease))
            .OrderByDescending(static candidate => candidate.Version)
            .ToArray();

        if (options.InstallPolicy == SetupInstallPolicy.IfNeeded && compatibleInstalled.Count > 0)
        {
            InstalledCandidate selected = compatibleInstalled[0];
            string? failure = await EnsureRetainedDependencyAsync(selected.Entry, options.Check, options.Source, dependency.PackageId, cancellationToken);
            if (failure is not null)
            {
                return SetupDependencyResult.Failed(dependency, failure);
            }

            return SetupDependencyResult.Satisfied(
                dependency,
                selected.Entry.PackageId,
                selected.Version.ToNormalizedString(),
                $"{dependency.DisplayName} is already installed.");
        }

        CatalogResolution resolution = await ResolveLatestFromCatalogAsync(dependency, options.Source, options.IncludePrerelease, cancellationToken);
        if (resolution.FailureMessage is not null)
        {
            if (compatibleInstalled.Count > 0)
            {
                InstalledCandidate selected = compatibleInstalled[0];
                string? failure = await EnsureRetainedDependencyAsync(selected.Entry, options.Check, options.Source, dependency.PackageId, cancellationToken);
                if (failure is not null)
                {
                    return SetupDependencyResult.Failed(dependency, failure);
                }

                return SetupDependencyResult.SatisfiedFallback(
                    dependency,
                    selected.Entry.PackageId,
                    selected.Version.ToNormalizedString(),
                    $"{dependency.DisplayName} is satisfied by installed version {selected.Version.ToNormalizedString()} because catalog resolution failed: {resolution.FailureMessage}");
            }

            if (resolution.PackageMissing && dependency.Optional && dependency.VersionRange is null)
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
            string? failure = await EnsureRetainedDependencyAsync(exactInstalled, options.Check, package.Source.Source, package.PackageId, cancellationToken);
            if (failure is not null)
            {
                return SetupDependencyResult.Failed(dependency, failure);
            }

            return SetupDependencyResult.Satisfied(
                dependency,
                exactInstalled.PackageId,
                targetVersion,
                $"{dependency.DisplayName} {targetVersion} is already installed.") with { Warning = channelWarning };
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
            string? payloadFailure = ValidateHostPayload(installResult.Entry);
            if (payloadFailure is not null)
            {
                return SetupDependencyResult.Failed(dependency, payloadFailure);
            }

            return installResult.AlreadyInstalled
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

    private string? ValidateHostPayload(WorkloadEntry entry)
    {
        if (!entry.PackageId.StartsWith(HostWorkloadPackage.PackageIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string directory = _workloadPaths.GetInstallDirectory(entry.PackageId, entry.PackageVersion);
        string contentRoot = WorkloadPackageLayout.GetContentRoot(directory, entry.RuntimeIdentifier);
        string executable = HostProcessStartInfoFactory.ResolveExecutablePath(contentRoot);
        return _fileSystem.FileExists(executable)
            ? null
            : $"Host workload executable was not found at '{executable}'. "
                + $"Reinstall the original host workload package from '{entry.Source}' to repair the installation.";
    }

    private async Task<string?> EnsureRetainedDependencyAsync(WorkloadEntry entry, bool checkOnly, string? requestedSource,
        string requestedPackageId, CancellationToken cancellationToken)
    {
        string? payloadFailure = ValidateHostPayload(entry);
        if (payloadFailure is not null)
        {
            return payloadFailure;
        }

        string actualSource = entry.LogicalPackage is { } logical
            && string.Equals(requestedPackageId, logical.PackageId, StringComparison.OrdinalIgnoreCase) ? logical.Source : entry.Source;
        bool bundled = entry.BundledTemplatesDeclared || _workloadInstaller.HasBundledTemplates(entry);
        if (bundled && requestedSource is not null && !string.Equals(requestedSource, actualSource, StringComparison.Ordinal))
        {
            return $"The requested source conflicts with the installed bundled stack's validated origin. Use its original source '{actualSource}' to repair it.";
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
