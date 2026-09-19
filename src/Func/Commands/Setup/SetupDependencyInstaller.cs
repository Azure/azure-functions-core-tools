// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates;
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
    IWorkloadInstaller workloadInstaller) : ISetupDependencyInstaller
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));
    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));

    public async Task<SetupDependencyResult> EnsureDependencyAsync(
        SetupCommandOptions options,
        SetupDependency dependency,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dependency);

        IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
        IReadOnlyList<InstalledCandidate> compatibleInstalled = GetInstalledCandidates(installed, dependency, options.IncludePrerelease)
            .Where(candidate => dependency.VersionRange is null || dependency.VersionRange.Satisfies(candidate.Version))
            .OrderByDescending(static candidate => candidate.Version)
            .ToArray();

        if (options.InstallPolicy == SetupInstallPolicy.IfNeeded && compatibleInstalled.Count > 0)
        {
            if (GetTemplatesReadinessFailure(installed, dependency) is { } readinessFailure)
            {
                return readinessFailure;
            }

            InstalledCandidate selected = compatibleInstalled[0];
            return SetupDependencyResult.Satisfied(
                dependency,
                selected.Entry.PackageId,
                selected.Version.ToNormalizedString(),
                $"{dependency.DisplayName} is already installed.");
        }

        CatalogResolution resolution = await ResolveLatestFromCatalogAsync(dependency, options.Source, options.IncludePrerelease, cancellationToken);
        if (resolution.FailureMessage is not null)
        {
            if (GetTemplatesReadinessFailure(installed, dependency) is { } readinessFailure)
            {
                return readinessFailure with
                {
                    Message = $"{readinessFailure.Message} Catalog resolution failed. {resolution.FailureMessage}",
                };
            }

            if (compatibleInstalled.Count > 0)
            {
                InstalledCandidate selected = compatibleInstalled[0];
                return SetupDependencyResult.SatisfiedFallback(
                    dependency,
                    selected.Entry.PackageId,
                    selected.Version.ToNormalizedString(),
                    $"{dependency.DisplayName} is satisfied by installed version {selected.Version.ToNormalizedString()} because catalog resolution failed: {resolution.FailureMessage}");
            }

            if (dependency.Kind == SetupDependencyKind.Templates)
            {
                foreach (WorkloadEntry entry in installed)
                {
                    if (MatchesDependencyPackageId(entry, dependency)
                        && NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? version)
                        && (dependency.Channel is null || BundleHelpers.MatchesChannel(version, dependency.Channel.Value))
                        && TemplatesWorkloadPolicy.GetMismatch(entry, dependency.Name) is { } mismatch)
                    {
                        return TemplatesMismatchFailure(dependency, entry, mismatch,
                            $"Catalog resolution failed. {resolution.FailureMessage} No installed workload was removed.");
                    }
                }
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

        if (GetTemplatesReadinessFailure(installed, dependency, package.Version) is { } resolvedReadinessFailure)
        {
            return resolvedReadinessFailure with { Warning = channelWarning };
        }

        // Match the exact resolved version directly: the version string already
        // encodes the channel, so this stays correct when a channeled dependency
        // fell back to the stable channel (where the installed version's channel
        // no longer equals dependency.Channel).
        bool exactInstalled = IsExactVersionInstalled(installed, dependency, package.Version);

        string targetVersion = package.Version.ToNormalizedString();
        if (exactInstalled)
        {
            return SetupDependencyResult.Satisfied(
                dependency,
                package.PackageId,
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

            // Search metadata can describe a different version from the one selected
            // on this channel. Only the returned registry entry describes this install.
            if (dependency.Kind == SetupDependencyKind.Templates)
            {
                string? mismatch = !MatchesPackageId(installResult.Entry, package.PackageId)
                    ? $"The returned package does not match the requested package '{package.PackageId}' for '{dependency.Name}-templates'"
                    : !NuGetVersion.TryParse(installResult.Entry.PackageVersion, out NuGetVersion? returnedVersion)
                        || !returnedVersion.Equals(package.Version)
                        ? $"The returned version does not match requested version '{targetVersion}' for '{dependency.Name}-templates'"
                        : TemplatesWorkloadPolicy.GetMismatch(installResult.Entry, dependency.Name);
                if (mismatch is not null)
                {
                    string context = installResult.AlreadyInstalled
                        ? "Failure detected after install. The workload was already installed. No rollback was performed."
                        : "Failure detected after install. No rollback was performed.";
                    return TemplatesMismatchFailure(dependency, installResult.Entry, mismatch, context) with { Warning = channelWarning };
                }

                IReadOnlyList<WorkloadEntry> updated = await _workloadStore.GetWorkloadsAsync(cancellationToken);
                if (GetTemplatesReadinessFailure(updated, dependency, package.Version, afterInstall: true) is { } installedReadinessFailure)
                {
                    return installedReadinessFailure with { Warning = channelWarning };
                }
            }

            string installedVersion = installResult.Entry.PackageVersion;
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
        catch (InvalidWorkloadSourceException ex)
        {
            throw new SetupConfigurationException(ex.Message, ex);
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
        catch (InvalidWorkloadSourceException ex)
        {
            throw new SetupConfigurationException(ex.Message, ex);
        }
        catch (Exception ex) when (
            ex is IOException
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

    private static bool IsExactVersionInstalled(IReadOnlyList<WorkloadEntry> installed, SetupDependency dependency, NuGetVersion version)
    {
        foreach (WorkloadEntry entry in installed)
        {
            if (MatchesDependency(entry, dependency)
                && NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? installedVersion)
                && installedVersion.Equals(version))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesDependency(WorkloadEntry entry, SetupDependency dependency)
        => MatchesDependencyPackageId(entry, dependency)
            && (dependency.Kind != SetupDependencyKind.Templates || TemplatesWorkloadPolicy.GetMismatch(entry, dependency.Name) is null);

    private static bool MatchesDependencyPackageId(WorkloadEntry entry, SetupDependency dependency)
    {
        if (dependency.ResolvedPackageId is { } resolvedPackageId
            && MatchesPackageId(entry, resolvedPackageId))
        {
            return true;
        }

        return MatchesPackageId(entry, dependency.PackageId);
    }

    private static SetupDependencyResult? GetTemplatesReadinessFailure(
        IReadOnlyList<WorkloadEntry> installed, SetupDependency dependency, NuGetVersion? selectedVersion = null, bool afterInstall = false)
    {
        if (dependency.Kind != SetupDependencyKind.Templates)
        {
            return null;
        }

        string targetId = dependency.ResolvedPackageId ?? dependency.PackageId;
        BundleChannel? channel = dependency.Channel is null || selectedVersion is null
            ? dependency.Channel : BundleHelpers.GetBundleChannel(selectedVersion);
        IReadOnlyList<WorkloadEntry> candidates = installed;
        if (selectedVersion is not null && !afterInstall && !IsExactVersionInstalled(installed, dependency, selectedVersion)
            && !HasDifferentTemplatesIncumbent(installed, dependency.Name, channel, targetId))
        {
            // Check prospective ownership only after preserving the actual installed
            // consumer. A synthetic conventional package must not displace it.
            candidates = [.. installed, new WorkloadEntry
            {
                PackageId = targetId,
                PackageVersion = selectedVersion.ToNormalizedString(),
                Kind = WorkloadKind.Content,
                Aliases = [$"{dependency.Name.Trim().ToLowerInvariant()}-templates"],
            }];
        }

        IReadOnlyList<WorkloadEntry> selected;
        string reason;
        try
        {
            selected = TemplatesWorkloadPolicy.Select(candidates, dependency.Name, channel);
            bool targetSelected = selected.Count > 0
                && string.Equals(selected[0].LogicalPackage?.PackageId ?? selected[0].PackageId, targetId, StringComparison.OrdinalIgnoreCase);
            if (targetSelected && (!afterInstall || selected.Any(entry =>
                    NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? version) && version.Equals(selectedVersion))))
            {
                return null;
            }

            if (selected.Count == 0 && !afterInstall)
            {
                return null;
            }

            reason = targetSelected || selected.Count == 0
                ? "The requested templates version is not available to the consumer."
                : $"The consumer selects owner '{selected[0].LogicalPackage?.PackageId ?? selected[0].PackageId}' instead. Explicit migration is required before changing templates owners.";
        }
        catch (InvalidOperationException ex)
        {
            selected = TemplatesWorkloadPolicy.GetClaimants(installed, dependency.Name, channel);
            reason = ex.Message;
        }

        string details = string.Join(" ", selected.Select(entry =>
        {
            string ownerId = entry.LogicalPackage?.PackageId ?? entry.PackageId;
            string ownerVersion = entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion;
            IReadOnlyList<string> aliases = entry.LogicalPackage?.Aliases ?? entry.Aliases;
            return $"Installed package '{entry.PackageId}' version '{entry.PackageVersion}', effective owner '{ownerId}' "
                + $"version '{ownerVersion}', aliases [{string.Join(", ", aliases)}]. "
                + $"To migrate explicitly, run 'func workload uninstall {ownerId} --version {ownerVersion} --exact', then retry setup.";
        }));
        string context = afterInstall
            ? "Failure detected after install. No rollback was performed."
            : "No installed workload was removed. No additional templates were installed.";
        return SetupDependencyResult.Failed(dependency,
            $"Templates alias '{dependency.Name.Trim().ToLowerInvariant()}-templates' for target '{targetId}' is blocked. {reason} {details} {context}")
            with { PackageId = selected.FirstOrDefault()?.PackageId, Version = selected.FirstOrDefault()?.PackageVersion };
    }

    private static bool HasDifferentTemplatesIncumbent(
        IReadOnlyList<WorkloadEntry> installed, string stack, BundleChannel? channel, string targetId)
    {
        IReadOnlyList<WorkloadEntry> incumbent;
        try
        {
            incumbent = TemplatesWorkloadPolicy.Select(installed, stack, channel);
        }
        catch (InvalidOperationException)
        {
            // Ambiguous or unusable installed rows do not establish an incumbent.
            // Leave their existing prospective repair/validation behavior intact.
            return false;
        }

        return incumbent.Count > 0 && !string.Equals(
            incumbent[0].LogicalPackage?.PackageId ?? incumbent[0].PackageId, targetId, StringComparison.OrdinalIgnoreCase);
    }

    private static SetupDependencyResult TemplatesMismatchFailure(
        SetupDependency dependency, WorkloadEntry entry, string mismatch, string context)
    {
        string uninstallId = entry.LogicalPackage?.PackageId ?? entry.PackageId;
        string uninstallVersion = entry.LogicalPackage?.PackageVersion ?? entry.PackageVersion;
        return SetupDependencyResult.Failed(dependency,
            $"Template metadata mismatch for '{entry.PackageId}' version '{entry.PackageVersion}'. {mismatch}. {context} "
            + $"To remove this version, run 'func workload uninstall {uninstallId} --version {uninstallVersion} --exact'.")
            with { PackageId = entry.PackageId, Version = entry.PackageVersion };
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
