// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Workload;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting.FirstRun;
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
    IHostJsonBundleSectionReader hostJsonBundleSectionReader,
    IFirstRunStateStore? firstRunStateStore = null) : ISetupRunner
{
    private const string DotNetFeature = "dotnet";
    private const string DotNetProfileRuntime = "dotnet-isolated";

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadStore _workloadStore = workloadStore ?? throw new ArgumentNullException(nameof(workloadStore));
    private readonly IWorkloadCatalog _workloadCatalog = workloadCatalog ?? throw new ArgumentNullException(nameof(workloadCatalog));
    private readonly IWorkloadInstaller _workloadInstaller = workloadInstaller ?? throw new ArgumentNullException(nameof(workloadInstaller));
    private readonly IProfileCatalog _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
    private readonly IOptionsMonitor<ProjectProfileOptions> _projectProfileOptions = projectProfileOptions ?? throw new ArgumentNullException(nameof(projectProfileOptions));
    private readonly IOptionsMonitor<UserProfilePreferenceOptions> _userProfilePreferenceOptions = userProfilePreferenceOptions ?? throw new ArgumentNullException(nameof(userProfilePreferenceOptions));
    private readonly ICliConfigurationProvider _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
    private readonly IHostJsonBundleSectionReader _hostJsonBundleSectionReader = hostJsonBundleSectionReader ?? throw new ArgumentNullException(nameof(hostJsonBundleSectionReader));
    private readonly IFirstRunStateStore? _firstRunStateStore = firstRunStateStore;

    public async Task<SetupRunResult> RunAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var renderer = new SetupRenderer(_interaction, options.OutputMode);
        if (options.IncludePrerelease && options.OutputMode != SetupOutputMode.Json)
        {
            _interaction.WriteHint(WorkloadInstallCommand.PrereleasePreviewHint);
        }
        try
        {
            SetupFeaturePlan? featurePlan = await ResolveFeaturesAsync(options, cancellationToken);
            if (featurePlan is null)
            {
                // The interactive default-features prompt was confirmed with no
                // selection. Treat that as a clean opt-out, not a failure: the
                // user explicitly chose to leave without installing anything.
                renderer.SetupSkippedNoSelection();
                await TryMarkFirstRunCompleteAsync(cancellationToken);
                return new SetupRunResult(0);
            }

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
            await TryMarkFirstRunCompleteAsync(cancellationToken);
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

    private async Task TryMarkFirstRunCompleteAsync(CancellationToken cancellationToken)
    {
        if (_firstRunStateStore is null)
        {
            return;
        }

        try
        {
            await _firstRunStateStore.MarkCompleteAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Failing to mark the first-run marker after a successful setup
            // is a minor nuisance (the user might see the first-run prompt
            // one more time), not a setup failure. Stay silent.
        }
    }

    private async Task<ProfileSetupOutcome> RunProfileAsync(SetupCommandOptions options, SetupFeaturePlan featurePlan, SetupProfileScope profileScope, SetupRenderer renderer, CancellationToken cancellationToken)
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

    private async Task<SetupFeaturePlan?> ResolveFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
    {
        IReadOnlyList<string>? requestedFeatures = options.Features.Count == 0
            ? await GetDefaultFeaturesAsync(options, cancellationToken)
            : options.Features;

        if (requestedFeatures is null)
        {
            // Interactive default-features prompt confirmed with no selection;
            // the caller treats this as a graceful exit.
            return null;
        }

        if (requestedFeatures.Count == 0)
        {
            throw new SetupConfigurationException("At least one setup feature is required.");
        }

        List<string> features = [];
        HashSet<string> featureNames = new(StringComparer.OrdinalIgnoreCase);
        List<SetupRuntimeFeature> runtimeFeatures = [];
        HashSet<string> runtimeFeatureNames = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> workerRuntimes = new(StringComparer.OrdinalIgnoreCase);
        bool includeExtensionBundle = false;

        foreach (string rawFeature in requestedFeatures)
        {
            string feature = NormalizeFeature(rawFeature);

            switch (feature)
            {
                case "host":
                    AddFeature(features, featureNames, "host");
                    break;

                case "runtime":
                    if (AddFeature(features, featureNames, "runtime"))
                    {
                        includeExtensionBundle = true;
                    }

                    break;

                case DotNetFeature:
                case DotNetProfileRuntime:
                    if (AddFeature(features, featureNames, DotNetFeature))
                    {
                        AddRuntimeFeature(runtimeFeatures, runtimeFeatureNames, DotNetFeature, DotNetProfileRuntime, installWorker: false);
                    }

                    break;

                case ".net":
                    throw new SetupConfigurationException($"The '{rawFeature}' feature is not supported. Use 'dotnet'.");

                case "dotnet-inprocess":
                    throw new SetupConfigurationException($"The '{rawFeature}' feature is not supported. Use 'dotnet'.");

                default:
                    if (!AddFeature(features, featureNames, feature))
                    {
                        break;
                    }

                    AddRuntimeFeature(runtimeFeatures, runtimeFeatureNames, feature, profileRuntime: feature, installWorker: true);
                    workerRuntimes.Add(feature);
                    if (GetBundlePolicy(feature) == SetupBundlePolicy.DefaultStable)
                    {
                        includeExtensionBundle = true;
                    }

                    break;
            }
        }

        return new SetupFeaturePlan(
            [.. features],
            [.. runtimeFeatures],
            [.. workerRuntimes.OrderBy(static runtime => runtime, StringComparer.OrdinalIgnoreCase)],
            includeExtensionBundle);
    }

    private async Task<IReadOnlyList<string>?> GetDefaultFeaturesAsync(SetupCommandOptions options, CancellationToken cancellationToken)
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
            StackChoicesResult choices = await BuildStackChoicesAsync(cancellationToken);

            // Render installed stacks as static "fake checkbox" lines above
            // the prompt so they're visible in context but cannot be toggled
            // (Spectre's MultiSelectionPrompt has no read-only items, and a
             // toggle visually implies an uninstall that `func setup` doesn't do).
            if (choices.InstalledStacks.Count > 0)
            {
                _interaction.WriteBlankLine();
                _interaction.WriteLine(l => l.Muted("Already installed (use `func workload uninstall <name>` to remove):"));
                foreach (string stack in choices.InstalledStacks)
                {
                    _interaction.WriteLine(l => l.Muted($"   [✓] {stack}"));
                }

                _interaction.WriteBlankLine();
            }

            if (choices.PromptChoices.Count == 0)
            {
                // Every supported stack is already installed; nothing to
                // offer. Treat as a clean opt-out so the caller marks the
                // first-run flag and exits without prompting.
                return null;
            }

            IReadOnlyList<string> picked = await _interaction.PromptForMultiSelectionAsync(
                "Select stacks to install (SPACE to toggle, ENTER to confirm; ENTER with no selection exits):",
                choices.PromptChoices,
                cancellationToken);

            // No selection = user opted out. Signal that with null so the
            // caller can exit cleanly instead of falling through to a default
            // they did not ask for.
            return picked.Count > 0 ? picked : null;
        }

        return ["runtime"];
    }

    private async Task<StackChoicesResult> BuildStackChoicesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> stacks = SetupDependency.Stacks;
        HashSet<string> installedStackPackageIds;
        try
        {
            IReadOnlyList<WorkloadEntry> installed = await _workloadStore.GetWorkloadsAsync(cancellationToken);
            installedStackPackageIds = new HashSet<string>(
                installed.Select(static entry => entry.PackageId),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Surfacing installed stacks is a UX hint, not a contract. If we
            // can't read the store, fall back to showing every stack as
            // available so the user can still make a selection.
            installedStackPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        List<MultiSelectionChoice> promptChoices = [];
        List<string> installedStacks = [];
        foreach (string stack in stacks.OrderBy(static stack => stack, StringComparer.OrdinalIgnoreCase))
        {
            if (installedStackPackageIds.Contains(SetupDependency.Stack(stack).PackageId))
            {
                installedStacks.Add(stack);
            }
            else
            {
                promptChoices.Add(new MultiSelectionChoice(stack, stack));
            }
        }

        return new StackChoicesResult(promptChoices, installedStacks);
    }

    private readonly record struct StackChoicesResult(
        IReadOnlyList<MultiSelectionChoice> PromptChoices,
        IReadOnlyList<string> InstalledStacks);

    private async Task<IReadOnlyList<SetupProfileScope>> ResolveProfileScopesAsync(SetupCommandOptions options, SetupRenderer renderer, CancellationToken cancellationToken)
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

    private SetupProfileScope CreateProfileScope(string profileName, IReadOnlyList<ProfileSourceSnapshot> snapshots, SetupRenderer renderer)
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

    private async Task<SetupDependencyPlan> BuildDependencyPlanAsync(DirectoryInfo workingDirectory, SetupFeaturePlan featurePlan, SetupProfileScope profileScope, CancellationToken cancellationToken)
    {
        List<SetupDependency> dependencies = [];
        List<SetupDependencyResult> failures = [];

        dependencies.Add(SetupDependency.Host(profileScope.Profile?.HostVersionRange));

        foreach (SetupRuntimeFeature runtimeFeature in featurePlan.RuntimeFeatures)
        {
            if (profileScope.Profile?.SupportedRuntimes is { } supportedRuntimes
                && !supportedRuntimes.Any(runtime => string.Equals(runtime, runtimeFeature.ProfileRuntime, StringComparison.OrdinalIgnoreCase)))
            {
                SetupDependency dependency = runtimeFeature.InstallWorker
                    ? SetupDependency.Worker(runtimeFeature.Name, versionRange: null)
                    : SetupDependency.Runtime(runtimeFeature.Name);
                string message = $"Profile '{profileScope.Profile.Name}' does not support runtime '{runtimeFeature.Name}'. "
                    + $"Supported runtimes: {string.Join(", ", supportedRuntimes)}.";
                failures.Add(SetupDependencyResult.Failed(dependency, message));
                continue;
            }

            if (runtimeFeature.InstallWorker)
            {
                VersionRange? workerRange = null;
                profileScope.Profile?.WorkerVersionRanges.TryGetValue(runtimeFeature.ProfileRuntime, out workerRange);
                dependencies.Add(SetupDependency.Worker(runtimeFeature.Name, workerRange));
            }

            if (SetupDependency.SupportsStack(runtimeFeature.Name))
            {
                dependencies.Add(SetupDependency.Stack(runtimeFeature.Name));
            }

            if (SetupDependency.SupportsTemplates(runtimeFeature.Name))
            {
                dependencies.Add(SetupDependency.Templates(runtimeFeature.Name));
            }
        }

        if (featurePlan.IncludeExtensionBundle)
        {
            SetupDependencyResult? failure = null;
            SetupDependency? bundleDependency = await TryCreateBundleDependencyAsync(workingDirectory, profileScope, cancellationToken);
            if (bundleDependency is null)
            {
                var dependency = SetupDependency.Bundle(BundleHelpers.StableBundleId, versionRange: null, rangeText: null);
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
            return SetupDependency.Bundle(BundleHelpers.StableBundleId, profileRange, profileRangeText);
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

            if (resolution.PackageMissing && dependency.Optional)
            {
                return SetupDependencyResult.Skipped(
                    dependency,
                    $"Skipped {dependency.DisplayName}: no workload package published for this runtime.");
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
                return CatalogResolution.Missing($"No {dependency.DisplayName} workload version{range} is available from the configured workload catalog.");
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

    private static bool AddFeature(List<string> features, HashSet<string> featureNames, string feature)
    {
        if (!featureNames.Add(feature))
        {
            return false;
        }

        features.Add(feature);
        return true;
    }

    private static void AddRuntimeFeature(
        List<SetupRuntimeFeature> runtimeFeatures,
        HashSet<string> runtimeFeatureNames,
        string name,
        string profileRuntime,
        bool installWorker)
    {
        if (runtimeFeatureNames.Add(name))
        {
            runtimeFeatures.Add(new SetupRuntimeFeature(name, profileRuntime, installWorker));
        }
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
        => IsDotNetRuntime(workerRuntime)
            ? SetupBundlePolicy.NotSupported
            : SetupBundlePolicy.DefaultStable;

    private static bool IsDotNetRuntime(string runtime)
        => string.Equals(runtime, DotNetFeature, StringComparison.OrdinalIgnoreCase)
            || string.Equals(runtime, DotNetProfileRuntime, StringComparison.OrdinalIgnoreCase);

    private sealed record CatalogResolution(ResolvedPackage? Package, string? FailureMessage, bool PackageMissing)
    {
        public static CatalogResolution Resolved(ResolvedPackage package) => new(package, FailureMessage: null, PackageMissing: false);

        public static CatalogResolution Failed(string failureMessage) => new(Package: null, failureMessage, PackageMissing: false);

        public static CatalogResolution Missing(string failureMessage) => new(Package: null, failureMessage, PackageMissing: true);
    }

    private sealed record InstalledCandidate(WorkloadEntry Entry, NuGetVersion Version);
}

internal sealed record SetupFeaturePlan(
    IReadOnlyList<string> Features,
    IReadOnlyList<SetupRuntimeFeature> RuntimeFeatures,
    IReadOnlyList<string> WorkerRuntimes,
    bool IncludeExtensionBundle);

internal sealed record SetupRuntimeFeature(string Name, string ProfileRuntime, bool InstallWorker);

internal sealed record SetupProfileScope(ResolvedProfile? Profile)
{
    public static SetupProfileScope Unconstrained { get; } = new(Profile: null);

    public string Name => Profile?.Name ?? "unconstrained";
}

internal sealed record SetupDependencyPlan(IReadOnlyList<SetupDependency> Dependencies, IReadOnlyList<SetupDependencyResult> Failures);

internal sealed record SetupDependency(
    SetupDependencyKind Kind,
    string Name,
    string DisplayName,
    string PackageId,
    VersionRange? VersionRange,
    string? RangeText,
    string? ResolvedPackageId,
    bool Optional = false)
{
    private const string WorkerPackagePrefix = "Azure.Functions.Cli.Workloads.Workers.";
    private const string StackPackagePrefix = "Azure.Functions.Cli.Workloads.";
    private const string TemplatesPackagePrefix = "Azure.Functions.Cli.Workloads.Templates.";

    // TODO: this should not be hardcoded in the CLI; discover the stack package
    // from the catalog (e.g. via an `alias:stack-<name>` tag) so new stacks don't
    // require a CLI release. Stacks not in this set (java, powershell, custom)
    // skip silently today.
    private static readonly HashSet<string> _stacks = new(StringComparer.OrdinalIgnoreCase) { "node", "python", "go", "dotnet" };

    // Stacks that publish a templates content workload (Azure.Functions.Cli.Workloads.Templates.*).
    // Go has no templates package today, so it is intentionally absent.
    private static readonly HashSet<string> _templates = new(StringComparer.OrdinalIgnoreCase) { "node", "python", "dotnet" };

    public static SetupDependency Host(VersionRange? versionRange)
        => new(
            SetupDependencyKind.Host,
            "host",
            "host",
            HostWorkloadPackage.CurrentPackageId,
            versionRange,
            SetupRunnerRangeText(versionRange),
            ResolvedPackageId: null);

    public static SetupDependency Runtime(string runtime)
        => new(
            SetupDependencyKind.Runtime,
            runtime,
            $"{runtime} runtime",
            runtime,
            VersionRange: null,
            RangeText: null,
            ResolvedPackageId: null);

    public static SetupDependency Worker(string runtime, VersionRange? versionRange)
        => new(
            SetupDependencyKind.Worker,
            runtime,
            $"{runtime} worker",
            SetupRunnerWorkerPackageId(runtime),
            versionRange,
            SetupRunnerRangeText(versionRange),
            ResolvedPackageId: null,
            Optional: true);

    public static SetupDependency Bundle(string bundleId, VersionRange? versionRange, string? rangeText)
        => new(
            SetupDependencyKind.ExtensionBundle,
            bundleId,
            "extension bundle",
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

    public static IReadOnlyList<string> Stacks => [.. _stacks];

    public static SetupDependency Templates(string stack)
        => new(
            SetupDependencyKind.Templates,
            stack,
            $"{stack} templates",
            TemplatesPackagePrefix + StackPackageSuffix(stack),
            VersionRange: null,
            RangeText: null,
            ResolvedPackageId: null,
            Optional: true);

    public static bool SupportsTemplates(string stack)
        => !string.IsNullOrWhiteSpace(stack) && _templates.Contains(stack.Trim());

    private static string StackPackageSuffix(string stack)
        => stack.Trim().ToLowerInvariant() switch
        {
            "dotnet" => "DotNet",
            "node" => "Node",
            "python" => "Python",
            "go" => "Go",
            _ => stack,
        };

    private static string SetupRunnerWorkerPackageId(string runtime) => WorkerPackagePrefix + runtime;

    private static string? SetupRunnerRangeText(VersionRange? range)
        => range is null ? null : range.OriginalString ?? range.ToString();

}

internal enum SetupDependencyKind
{
    Host,
    Runtime,
    Worker,
    Stack,
    Templates,
    ExtensionBundle,
}

internal enum SetupDependencyStatus
{
    Satisfied,
    Installed,
    SatisfiedFallback,
    Skipped,
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

    public static SetupDependencyResult Skipped(SetupDependency dependency, string message)
        => new(dependency, SetupDependencyStatus.Skipped, dependency.PackageId, Version: null, message);

    public static SetupDependencyResult Failed(SetupDependency dependency, string message)
        => new(
            dependency,
            SetupDependencyStatus.Failed,
            dependency.Kind == SetupDependencyKind.Runtime ? null : dependency.PackageId,
            Version: null,
            message);
}

internal sealed record ProfileSetupOutcome(int FailureCount);

internal enum SetupBundlePolicy
{
    NotSupported,
    DefaultStable,
}

internal sealed class SetupConfigurationException(string message) : Exception(message);
