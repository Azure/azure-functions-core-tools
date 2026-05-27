// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Discovery;
using Azure.Functions.Cli.Workloads.Install;
using Azure.Functions.Cli.Workloads.Storage;
using Microsoft.Extensions.Options;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Setup;

internal sealed class SetupRunner(
    IInteractionService interaction,
    IWorkloadStore workloadStore,
    IWorkloadCatalog workloadCatalog,
    IWorkloadInstaller workloadInstaller,
    IProfileCatalog profileCatalog,
    IOptionsMonitor<ProjectProfileOptions> projectProfileOptions,
    IOptionsMonitor<UserProfilePreferenceOptions> userProfilePreferenceOptions,
    ICliConfigurationProvider configurationProvider,
    IHostJsonBundleSectionReader hostJsonBundleSectionReader) : ISetupRunner
{
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));
    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));
    private readonly IProfileCatalog _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
    private readonly IOptionsMonitor<ProjectProfileOptions> _projectProfileOptions = projectProfileOptions ?? throw new ArgumentNullException(nameof(projectProfileOptions));
    private readonly IOptionsMonitor<UserProfilePreferenceOptions> _userProfilePreferenceOptions = userProfilePreferenceOptions ?? throw new ArgumentNullException(nameof(userProfilePreferenceOptions));
    private readonly ICliConfigurationProvider _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
    private readonly IHostJsonBundleSectionReader _hostJsonBundleSectionReader = hostJsonBundleSectionReader ?? throw new ArgumentNullException(nameof(hostJsonBundleSectionReader));

    public async Task<SetupRunResult> RunAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var renderer = new SetupRenderer(_interaction, options.OutputMode);
        try
        {
            SetupFeaturePlan featurePlan = await ResolveFeaturesAsync(options, cancellationToken);
            IReadOnlyList<SetupProfileScope> profileScopes = await ResolveProfileScopesAsync(options, renderer, cancellationToken);

            renderer.SetupStarted(options, featurePlan, profileScopes);

            int failureCount = 0;
            foreach (SetupProfileScope profileScope in profileScopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                renderer.ProfileStarted(profileScope);

                ProfileSetupOutcome outcome = await RunProfileAsync(options, featurePlan, profileScope, renderer, cancellationToken);
                failureCount += outcome.FailureCount;
                renderer.ProfileCompleted(profileScope, outcome);

                if (outcome.FailureCount > 0 && !options.Check)
                {
                    renderer.SetupFailed(failureCount);
                    return new SetupRunResult(1);
                }
            }

            if (failureCount > 0)
            {
                renderer.SetupFailed(failureCount);
                return new SetupRunResult(1);
            }

            renderer.SetupCompleted();
            return new SetupRunResult(0);
        }
        catch (SetupConfigurationException ex)
        {
            renderer.SetupFailed(ex.Message);
            return new SetupRunResult(1);
        }
        catch (ProfileConfigurationException ex)
        {
            renderer.SetupFailed(ex.Message);
            return new SetupRunResult(1);
        }
        catch (ExtensionBundleConfigurationException ex)
        {
            renderer.SetupFailed(ex.Message);
            return new SetupRunResult(1);
        }
    }

    private async Task<ProfileSetupOutcome> RunProfileAsync(
        SetupCommandOptions options,
        SetupFeaturePlan featurePlan,
        SetupProfileScope profileScope,
        SetupRenderer renderer,
        CancellationToken cancellationToken)
    {
        SetupDependencyPlan plan = await BuildDependencyPlanAsync(options.WorkingDirectory, featurePlan, profileScope, cancellationToken);
        int failures = 0;

        foreach (SetupDependency dependency in plan.Dependencies)
        {
            renderer.DependencyDetected(profileScope, dependency);
            SetupDependencyResult result = await EnsureDependencyAsync(options, dependency, cancellationToken);
            renderer.DependencyResult(profileScope, dependency, result);

            if (result.Status == SetupDependencyStatus.Failed)
            {
                failures++;
                if (!options.Check)
                {
                    return new ProfileSetupOutcome(failures);
                }
            }
        }

        foreach (SetupDependencyResult failure in plan.Failures)
        {
            renderer.DependencyResult(profileScope, failure.Dependency, failure);
            failures++;
            if (!options.Check)
            {
                return new ProfileSetupOutcome(failures);
            }
        }

        return new ProfileSetupOutcome(failures);
    }

    private async Task<SetupFeaturePlan> ResolveFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> requestedFeatures = options.Features.Count == 0
            ? await GetDefaultFeaturesAsync(options, cancellationToken)
            : options.Features;

        if (requestedFeatures.Count == 0)
        {
            throw new SetupConfigurationException("At least one setup feature is required.");
        }

        HashSet<string> normalizedFeatures = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> workerRuntimes = new(StringComparer.OrdinalIgnoreCase);
        bool includeExtensionBundle = false;

        foreach (string rawFeature in requestedFeatures)
        {
            string feature = NormalizeFeature(rawFeature);
            if (!normalizedFeatures.Add(feature))
            {
                continue;
            }

            switch (feature)
            {
                case "host":
                    break;

                case "runtime":
                    includeExtensionBundle = true;
                    break;

                case "dotnet":
                case ".net":
                case "dotnet-isolated":
                    workerRuntimes.Add("dotnet-isolated");
                    break;

                case "dotnet-inprocess":
                    throw new SetupConfigurationException(".NET in-process is not supported. Use 'dotnet-isolated'.");

                default:
                    workerRuntimes.Add(feature);
                    if (GetBundlePolicy(feature) == SetupBundlePolicy.DefaultStable)
                    {
                        includeExtensionBundle = true;
                    }

                    break;
            }
        }

        return new SetupFeaturePlan(
            [.. normalizedFeatures],
            [.. workerRuntimes.OrderBy(static runtime => runtime, StringComparer.OrdinalIgnoreCase)],
            includeExtensionBundle);
    }

    private async Task<IReadOnlyList<string>> GetDefaultFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        string? configuredStack = _configurationProvider
            .GetProjectConfiguration(options.WorkingDirectory)
            [$"{CliConfigurationNames.StackSectionName}:{CliConfigurationNames.StackRuntimeKey}"];

        if (!string.IsNullOrWhiteSpace(configuredStack))
        {
            return [configuredStack.Trim()];
        }

        if (!options.NonInteractive && _interaction.IsInteractive)
        {
            string picked = await _interaction.PromptForSelectionAsync(
                "Select a stack to install:",
                SetupDependency.Stacks,
                cancellationToken);
            return [picked];
        }

        return ["runtime"];
    }

    private async Task<IReadOnlyList<SetupProfileScope>> ResolveProfileScopesAsync(
        SetupCommandOptions options,
        SetupRenderer renderer,
        CancellationToken cancellationToken)
    {
        string projectDirectory = Path.GetFullPath(options.WorkingDirectory.FullName);
        ProjectProfileOptions projectOptions = _projectProfileOptions.Get(projectDirectory);
        IReadOnlyList<string> explicitProfiles = NormalizeDistinct(options.ProfileNames);

        if (explicitProfiles.Count > 0)
        {
            IReadOnlyList<ProfileSourceSnapshot> snapshots = await _profileCatalog.LoadAsync(new ProfileSourceContext(options.WorkingDirectory), cancellationToken);
            List<SetupProfileScope> scopes = [];
            foreach (string profileName in explicitProfiles)
            {
                if (projectOptions.Profiles.Count > 0 && !IsDeclaredProfile(projectOptions, profileName))
                {
                    renderer.Warning($"Profile '{profileName}' is not declared in this project's .func/config.json.");
                }

                scopes.Add(CreateProfileScope(profileName, snapshots, renderer));
            }

            return scopes;
        }

        if (projectOptions.Profiles.Count > 0)
        {
            IReadOnlyList<ProfileSourceSnapshot> snapshots = await _profileCatalog.LoadAsync(new ProfileSourceContext(options.WorkingDirectory), cancellationToken);
            return [.. projectOptions.Profiles.Select(profileName => CreateProfileScope(profileName, snapshots, renderer))];
        }

        string? userDefaultProfile = NullIfWhiteSpace(_userProfilePreferenceOptions.CurrentValue.DefaultProfile);
        if (userDefaultProfile is not null)
        {
            IReadOnlyList<ProfileSourceSnapshot> snapshots = await _profileCatalog.LoadAsync(new ProfileSourceContext(options.WorkingDirectory), cancellationToken);
            return [CreateProfileScope(userDefaultProfile, snapshots, renderer)];
        }

        return [SetupProfileScope.Unconstrained];
    }

    private SetupProfileScope CreateProfileScope(
        string profileName,
        IReadOnlyList<ProfileSourceSnapshot> snapshots,
        SetupRenderer renderer)
    {
        ResolvedProfile profile = _profileCatalog.ResolveProfile(profileName, snapshots);
        if (profile.Status == ProfileStatus.Deprecated)
        {
            string suffix = string.IsNullOrWhiteSpace(profile.DeprecationUrl)
                ? string.Empty
                : $" See {profile.DeprecationUrl}.";
            renderer.Warning($"Profile '{profile.Name}' is deprecated.{suffix}");
        }

        return new SetupProfileScope(profile);
    }

    private async Task<SetupDependencyPlan> BuildDependencyPlanAsync(
        DirectoryInfo workingDirectory,
        SetupFeaturePlan featurePlan,
        SetupProfileScope profileScope,
        CancellationToken cancellationToken)
    {
        List<SetupDependency> dependencies = [];
        List<SetupDependencyResult> failures = [];

        dependencies.Add(SetupDependency.Host(profileScope.Profile?.HostVersionRange));

        foreach (string workerRuntime in featurePlan.WorkerRuntimes)
        {
            if (profileScope.Profile?.SupportedRuntimes is { } supportedRuntimes
                && !supportedRuntimes.Any(runtime => string.Equals(runtime, workerRuntime, StringComparison.OrdinalIgnoreCase)))
            {
                var dependency = SetupDependency.Worker(workerRuntime, versionRange: null);
                string message = $"Profile '{profileScope.Profile.Name}' does not support runtime '{workerRuntime}'. "
                    + $"Supported runtimes: {string.Join(", ", supportedRuntimes)}.";
                failures.Add(SetupDependencyResult.Failed(dependency, message));
                continue;
            }

            VersionRange? workerRange = null;
            profileScope.Profile?.WorkerVersionRanges.TryGetValue(workerRuntime, out workerRange);
            if (SetupDependency.SupportsWorker(workerRuntime))
            {
                dependencies.Add(SetupDependency.Worker(workerRuntime, workerRange));
            }

            if (SetupDependency.SupportsStack(workerRuntime))
            {
                dependencies.Add(SetupDependency.Stack(workerRuntime));
            }
        }

        if (featurePlan.IncludeExtensionBundle)
        {
            SetupDependencyResult? failure = null;
            SetupDependency? bundleDependency = await TryCreateBundleDependencyAsync(workingDirectory, profileScope, cancellationToken);
            if (bundleDependency is null)
            {
                var dependency = SetupDependency.Bundle(
                    InstalledBundleScanner.StableBundleId,
                    versionRange: null,
                    rangeText: null);
                failure = SetupDependencyResult.Failed(
                    dependency,
                    "The host.json extensionBundle range and profile extensionBundle range do not overlap.");
            }
            else
            {
                dependencies.Add(bundleDependency);
            }

            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        return new SetupDependencyPlan(dependencies, failures);
    }

    private async Task<SetupDependency?> TryCreateBundleDependencyAsync(
        DirectoryInfo workingDirectory,
        SetupProfileScope profileScope,
        CancellationToken cancellationToken)
    {
        HostJsonBundleSection? hostJsonBundle = await _hostJsonBundleSectionReader.ReadAsync(workingDirectory, cancellationToken);
        VersionRange? profileRange = profileScope.Profile?.ExtensionBundleVersionRange;
        string? profileRangeText = RangeText(profileRange);

        if (hostJsonBundle is null)
        {
            return SetupDependency.Bundle(
                InstalledBundleScanner.StableBundleId,
                profileRange,
                profileRangeText);
        }

        VersionRange? effectiveRange = VersionRangeIntersection.Intersect(hostJsonBundle.Version, profileRangeText);
        return effectiveRange is null
            ? null
            : SetupDependency.Bundle(hostJsonBundle.Id, effectiveRange, RangeText(effectiveRange));
    }

    private async Task<SetupDependencyResult> EnsureDependencyAsync(
        SetupCommandOptions options,
        SetupDependency dependency,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
        IReadOnlyList<InstalledCandidate> compatibleInstalled = GetInstalledCandidates(installed, dependency, options.IncludePrerelease)
            .Where(candidate => dependency.VersionRange is null || dependency.VersionRange.Satisfies(candidate.Version))
            .OrderByDescending(static candidate => candidate.Version)
            .ToArray();

        if (options.InstallPolicy == SetupInstallPolicy.IfNeeded && compatibleInstalled.Count > 0)
        {
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
            if (compatibleInstalled.Count > 0)
            {
                InstalledCandidate selected = compatibleInstalled[0];
                return SetupDependencyResult.SatisfiedFallback(
                    dependency,
                    selected.Entry.PackageId,
                    selected.Version.ToNormalizedString(),
                    $"{dependency.DisplayName} is satisfied by installed version {selected.Version.ToNormalizedString()} because catalog resolution failed: {resolution.FailureMessage}");
            }

            return SetupDependencyResult.Failed(dependency, resolution.FailureMessage);
        }

        ResolvedPackage package = resolution.Package!;
        dependency = dependency with { ResolvedPackageId = package.PackageId };
        InstalledCandidate? exactInstalled = GetInstalledCandidates(installed, dependency, options.IncludePrerelease)
            .FirstOrDefault(candidate => candidate.Version.Equals(package.Version));

        string targetVersion = package.Version.ToNormalizedString();
        if (exactInstalled is not null)
        {
            return SetupDependencyResult.Satisfied(
                dependency,
                package.PackageId,
                targetVersion,
                $"{dependency.DisplayName} {targetVersion} is already installed.");
        }

        if (options.Check)
        {
            return SetupDependencyResult.Failed(
                dependency,
                $"{dependency.DisplayName} {targetVersion} is not installed.");
        }

        try
        {
            WorkloadInstallResult installResult = await _workloadInstaller.InstallFromCatalogAsync(
                package.PackageId,
                package.Version,
                options.Source,
                includePrerelease: options.IncludePrerelease,
                exact: true,
                force: false,
                progress: null,
                cancellationToken);

            string installedVersion = installResult.Entry.PackageVersion;
            return installResult.AlreadyInstalled
                ? SetupDependencyResult.Satisfied(
                    dependency,
                    installResult.Entry.PackageId,
                    installedVersion,
                    $"{dependency.DisplayName} {installedVersion} is already installed.")
                : SetupDependencyResult.Installed(
                    dependency,
                    installResult.Entry.PackageId,
                    installedVersion,
                    $"Installed {dependency.DisplayName} {installedVersion}.");
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
                string range = dependency.RangeText is null ? string.Empty : $" in range '{dependency.RangeText}'";
                return CatalogResolution.Failed($"No {dependency.DisplayName} workload version{range} is available from the configured workload catalog.");
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

    private static IReadOnlyList<InstalledCandidate> GetInstalledCandidates(
        IReadOnlyList<WorkloadEntry> installed,
        SetupDependency dependency,
        bool includePrerelease)
    {
        List<InstalledCandidate> candidates = [];
        foreach (WorkloadEntry entry in installed)
        {
            if (!MatchesDependency(entry, dependency)
                || !NuGetVersion.TryParse(entry.PackageVersion, out NuGetVersion? version)
                || (!includePrerelease && version.IsPrerelease))
            {
                continue;
            }

            candidates.Add(new InstalledCandidate(entry, version));
        }

        return candidates;
    }

    private static bool MatchesDependency(WorkloadEntry entry, SetupDependency dependency)
    {
        if (dependency.ResolvedPackageId is { } resolvedPackageId
            && string.Equals(entry.PackageId, resolvedPackageId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(entry.PackageId, dependency.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsDeclaredProfile(ProjectProfileOptions projectOptions, string profile)
        => projectOptions.Profiles.Any(candidate => string.Equals(candidate, profile, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> NormalizeDistinct(IReadOnlyList<string> values)
    {
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            string? normalized = NullIfWhiteSpace(value);
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }

    private static string NormalizeFeature(string value)
    {
        string? normalized = NullIfWhiteSpace(value);
        if (normalized is null)
        {
            throw new SetupConfigurationException("Setup feature names cannot be empty.");
        }

        return normalized.ToLowerInvariant();
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? RangeText(VersionRange? range)
        => range is null ? null : range.OriginalString ?? range.ToString();

    private static SetupBundlePolicy GetBundlePolicy(string workerRuntime)
        => string.Equals(workerRuntime, "dotnet-isolated", StringComparison.OrdinalIgnoreCase)
            ? SetupBundlePolicy.NotSupported
            : SetupBundlePolicy.DefaultStable;

    private sealed record CatalogResolution(ResolvedPackage? Package, string? FailureMessage)
    {
        public static CatalogResolution Resolved(ResolvedPackage package) => new(package, FailureMessage: null);

        public static CatalogResolution Failed(string failureMessage) => new(Package: null, failureMessage);
    }

    private sealed record InstalledCandidate(WorkloadEntry Entry, NuGetVersion Version);
}

internal sealed record SetupFeaturePlan(
    IReadOnlyList<string> Features,
    IReadOnlyList<string> WorkerRuntimes,
    bool IncludeExtensionBundle);

internal sealed record SetupProfileScope(ResolvedProfile? Profile)
{
    public static SetupProfileScope Unconstrained { get; } = new(Profile: null);

    public string Name => Profile?.Name ?? "unconstrained";
}

internal sealed record SetupDependencyPlan(
    IReadOnlyList<SetupDependency> Dependencies,
    IReadOnlyList<SetupDependencyResult> Failures);

internal sealed record SetupDependency(
    SetupDependencyKind Kind,
    string Name,
    string DisplayName,
    string PackageId,
    VersionRange? VersionRange,
    string? RangeText,
    string? ResolvedPackageId)
{
    private const string WorkerPackagePrefix = "Azure.Functions.Cli.Workloads.Workers.";
    private const string StackPackagePrefix = "Azure.Functions.Cli.Workloads.";

    // TODO: this should not be hardcoded in the CLI; discover the stack package
    // from the catalog (e.g. via an `alias:stack-<name>` tag) so new stacks don't
    // require a CLI release. Stacks not in this set (java, powershell, custom)
    // skip silently today.
    private static readonly HashSet<string> _stacks =
        new(StringComparer.OrdinalIgnoreCase) { "node", "python", "go", "dotnet-isolated" };

    // Runtimes that ship a Workers.<runtime> workload package. Other runtimes
    // (dotnet-isolated uses the .NET SDK; java/powershell/custom have no worker
    // package today) skip worker install silently.
    private static readonly HashSet<string> _workers =
        new(StringComparer.OrdinalIgnoreCase) { "node", "python", "go" };

    public static SetupDependency Host(VersionRange? versionRange)
        => new(
            SetupDependencyKind.Host,
            "host",
            "host",
            HostWorkloadPackage.CurrentPackageId,
            versionRange,
            SetupRunnerRangeText(versionRange),
            ResolvedPackageId: null);

    public static SetupDependency Worker(string runtime, VersionRange? versionRange)
        => new(
            SetupDependencyKind.Worker,
            runtime,
            $"{runtime} worker",
            SetupRunnerWorkerPackageId(runtime),
            versionRange,
            SetupRunnerRangeText(versionRange),
            ResolvedPackageId: null);

    public static SetupDependency Bundle(string bundleId, VersionRange? versionRange, string? rangeText)
        => new(
            SetupDependencyKind.ExtensionBundle,
            bundleId,
            $"extension bundle {bundleId}",
            IInstalledBundleWorkloads.BundleWorkloadPackageId,
            versionRange,
            rangeText,
            ResolvedPackageId: null);

    public static SetupDependency Stack(string stack)
        => new(
            SetupDependencyKind.Stack,
            stack,
            $"{stack} stack",
            StackPackagePrefix + StackPackageSuffix(stack),
            VersionRange: null,
            RangeText: null,
            ResolvedPackageId: null);

    public static bool SupportsStack(string stack)
        => !string.IsNullOrWhiteSpace(stack) && _stacks.Contains(stack.Trim());

    public static bool SupportsWorker(string runtime)
        => !string.IsNullOrWhiteSpace(runtime) && _workers.Contains(runtime.Trim());

    public static IReadOnlyList<string> Stacks => [.. _stacks];

    // dotnet-isolated is the one stack whose package suffix doesn't match the
    // runtime identifier verbatim; the rest concat directly (NuGet package ids
    // are case-insensitive).
    private static string StackPackageSuffix(string stack)
        => string.Equals(stack, "dotnet-isolated", StringComparison.OrdinalIgnoreCase) ? "DotNet" : stack;

    private static string SetupRunnerWorkerPackageId(string runtime) => WorkerPackagePrefix + runtime;

    private static string? SetupRunnerRangeText(VersionRange? range)
        => range is null ? null : range.OriginalString ?? range.ToString();

}

internal enum SetupDependencyKind
{
    Host,
    Worker,
    Stack,
    ExtensionBundle,
}

internal enum SetupDependencyStatus
{
    Satisfied,
    Installed,
    SatisfiedFallback,
    Failed,
}

internal sealed record SetupDependencyResult(
    SetupDependency Dependency,
    SetupDependencyStatus Status,
    string? PackageId,
    string? Version,
    string Message)
{
    public static SetupDependencyResult Satisfied(SetupDependency dependency, string packageId, string version, string message)
        => new(dependency, SetupDependencyStatus.Satisfied, packageId, version, message);

    public static SetupDependencyResult Installed(SetupDependency dependency, string packageId, string version, string message)
        => new(dependency, SetupDependencyStatus.Installed, packageId, version, message);

    public static SetupDependencyResult SatisfiedFallback(SetupDependency dependency, string packageId, string version, string message)
        => new(dependency, SetupDependencyStatus.SatisfiedFallback, packageId, version, message);

    public static SetupDependencyResult Failed(SetupDependency dependency, string message)
        => new(dependency, SetupDependencyStatus.Failed, dependency.PackageId, Version: null, message);
}

internal sealed record ProfileSetupOutcome(int FailureCount);

internal enum SetupBundlePolicy
{
    NotSupported,
    DefaultStable,
}

internal sealed class SetupConfigurationException(string message) : Exception(message);
