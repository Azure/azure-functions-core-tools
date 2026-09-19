// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Setup;

internal interface ISetupDependencyPlanBuilder
{
    /// <summary>
    /// Expands a feature plan into the concrete dependencies to install for one
    /// profile scope, plus the failures detected while planning (unsupported
    /// runtime for the profile, non-overlapping bundle ranges).
    /// </summary>
    public Task<SetupDependencyPlan> BuildDependencyPlanAsync(
        SetupCommandOptions options,
        SetupFeaturePlan featurePlan,
        SetupProfileScope profileScope,
        CancellationToken cancellationToken);
}

internal sealed class SetupDependencyPlanBuilder(
    IHostJsonBundleSectionReader hostJsonBundleSectionReader,
    ISetupStackCatalog stackCatalog) : ISetupDependencyPlanBuilder
{
    private readonly IHostJsonBundleSectionReader _hostJsonBundleSectionReader = hostJsonBundleSectionReader ?? throw new ArgumentNullException(nameof(hostJsonBundleSectionReader));
    private readonly ISetupStackCatalog _stackCatalog = stackCatalog ?? throw new ArgumentNullException(nameof(stackCatalog));

    public async Task<SetupDependencyPlan> BuildDependencyPlanAsync(
        SetupCommandOptions options,
        SetupFeaturePlan featurePlan,
        SetupProfileScope profileScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(featurePlan);
        ArgumentNullException.ThrowIfNull(profileScope);

        DirectoryInfo workingDirectory = options.WorkingDirectory;

        // Only pay for catalog discovery when a runtime feature could map to a
        // stack or templates package. `func setup --features host` must not hit
        // the catalog at all.
        SetupStackSnapshot stacks = featurePlan.RuntimeFeatures.Count > 0
            ? await _stackCatalog.GetStacksAsync(options.Source, options.IncludePrerelease, cancellationToken)
            : SetupDependency.BuiltInStackSnapshot;

        List<SetupDependency> dependencies = [];
        List<SetupDependencyResult> failures = [];

        HostJsonBundleSection? hostJsonBundle = await _hostJsonBundleSectionReader.ReadAsync(workingDirectory, cancellationToken);
        BundleChannel bundleChannel = ResolveBundleChannel(hostJsonBundle);
        SetupStackSnapshot? channelTemplates = null;

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

            if (stacks.IsAmbiguous(runtimeFeature.Name))
            {
                failures.Add(CreateAliasConflictFailure(stacks, runtimeFeature.Name, runtimeFeature.Name));
                continue;
            }

            if (stacks.IsUnsupported(runtimeFeature.Name))
            {
                failures.Add(CreateUnsupportedRoleFailure(runtimeFeature.Name));
                continue;
            }

            string? stackPackageId = stacks.StackPackageId(runtimeFeature.Name);
            if (stackPackageId is null && SetupDependency.BuiltInStackSnapshot.SupportsStack(runtimeFeature.Name))
            {
                failures.Add(SetupDependencyResult.Failed(
                    SetupDependency.Runtime(runtimeFeature.Name),
                    $"The requested '{runtimeFeature.Name}' stack is not available from the selected workload catalog. "
                    + "Select a --source feed that publishes it, or install the intended package with 'func workload install --exact <package-id>'."));
                continue;
            }

            bool isDotNet = SetupRuntimes.IsDotNetRuntime(runtimeFeature.Name);
            SetupStackSnapshot templates = stacks;
            if (CreateTemplatesRestrictionFailure(stacks, runtimeFeature.Name) is { } templatesFailure)
            {
                failures.Add(templatesFailure);
                continue;
            }

            if (!isDotNet && !options.IncludePrerelease && hostJsonBundle is not null && bundleChannel != BundleChannel.Stable)
            {
                // A bundle channel opts templates into discovery, not stacks or
                // their canonical names, which remain governed by the CLI policy.
                SetupStackSnapshot inclusive = channelTemplates ??= await _stackCatalog.GetStacksAsync(options.Source, true, cancellationToken);
                if (CreateTemplatesRestrictionFailure(inclusive, runtimeFeature.Name) is { } inclusiveFailure)
                {
                    failures.Add(inclusiveFailure);
                    continue;
                }

                // A supplemental fallback is not new ownership evidence. Keep the
                // normal scan's package maps, including known absence of templates.
                templates = inclusive.IsFallback ? stacks : inclusive;
                string canonicalStack = stacks.CanonicalStackName(runtimeFeature.Name);
                string templatesCanonicalStack = templates.CanonicalStackName(runtimeFeature.Name);
                if (!string.Equals(canonicalStack, templatesCanonicalStack, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(SetupDependencyResult.Failed(
                        SetupDependency.Runtime(runtimeFeature.Name),
                        $"Stack '{canonicalStack}' resolves to '{templatesCanonicalStack}' during prerelease-inclusive templates discovery. "
                        + "Use a --source feed with consistent canonical stack aliases, or install the intended packages with 'func workload install --exact <package-id>'."));
                    continue;
                }

            }

            if (runtimeFeature.InstallWorker)
            {
                VersionRange? workerRange = null;
                profileScope.Profile?.WorkerVersionRanges.TryGetValue(runtimeFeature.ProfileRuntime, out workerRange);
                dependencies.Add(SetupDependency.Worker(runtimeFeature.Name, workerRange));
            }

            if (stackPackageId is not null)
            {
                dependencies.Add(SetupDependency.Stack(runtimeFeature.Name, stackPackageId));
            }

            if (templates.TemplatesPackageId(runtimeFeature.Name) is { } templatesPackageId)
            {
                // Script stacks ship per-channel templates that track the bundle
                // channel; dotnet templates don't use bundles, so they stay channel-less.
                BundleChannel? templatesChannel = isDotNet ? null : bundleChannel;
                dependencies.Add(SetupDependency.Templates(runtimeFeature.Name, templatesPackageId, templatesChannel));
            }
        }

        if (featurePlan.IncludeExtensionBundle)
        {
            SetupDependency? bundleDependency = CreateBundleDependency(hostJsonBundle, bundleChannel, profileScope);
            if (bundleDependency is null)
            {
                var dependency = SetupDependency.Bundle(BundleHelpers.StableBundleId, versionRange: null, rangeText: null, BundleChannel.Stable);
                failures.Add(SetupDependencyResult.Failed(
                    dependency,
                    "The host.json extensionBundle range and profile extensionBundle range do not overlap."));
            }
            else
            {
                dependencies.Add(bundleDependency);
            }
        }

        return new SetupDependencyPlan(dependencies, failures);
    }

    private static SetupDependencyResult? CreateTemplatesRestrictionFailure(SetupStackSnapshot snapshot, string runtime)
    {
        string[] names = [runtime, snapshot.CanonicalStackName(runtime)];
        foreach (string name in names)
        {
            string templatesAlias = $"{name}-templates";
            if (snapshot.IsAmbiguous(name) || snapshot.IsAmbiguous(templatesAlias))
            {
                return CreateAliasConflictFailure(snapshot, runtime, snapshot.IsAmbiguous(name) ? name : templatesAlias);
            }

            if (snapshot.IsUnsupported(name) || snapshot.IsUnsupported(templatesAlias))
            {
                return CreateUnsupportedRoleFailure(runtime);
            }
        }

        return null;
    }

    private static SetupDependencyResult CreateUnsupportedRoleFailure(string runtime)
        => SetupDependencyResult.Failed(SetupDependency.Runtime(runtime),
            $"Stack '{runtime}' uses a RID-specific stack or templates package that setup cannot discover safely. "
            + "Use a --source feed with portable stack and templates packages.");

    private static SetupDependencyResult CreateAliasConflictFailure(SetupStackSnapshot snapshot, string runtime, string alias)
    {
        IReadOnlyList<SetupAliasConflict> conflicts = snapshot.ConflictsFor(alias);
        string details = conflicts.Count == 0
            ? string.Empty
            : " Conflicting claims: " + string.Join("; ", conflicts.Select(conflict =>
                $"'{conflict.Alias}': {string.Join(", ", conflict.PackageIds)}")) + ".";
        return SetupDependencyResult.Failed(
            SetupDependency.Runtime(runtime),
            $"More than one workload package on this feed claims aliases used by '{runtime}' (workload-package alias collision)."
            + details
            + " Install the intended package with 'func workload install --exact <package-id>', "
            + "or point --source at a feed without conflicting claims.");
    }

    private static BundleChannel ResolveBundleChannel(HostJsonBundleSection? hostJsonBundle)
        => hostJsonBundle is not null && BundleHelpers.TryGetBundleChannel(hostJsonBundle.Id, out BundleChannel channel)
            ? channel
            : BundleChannel.Stable;

    private static SetupDependency? CreateBundleDependency(
        HostJsonBundleSection? hostJsonBundle,
        BundleChannel channel,
        SetupProfileScope profileScope)
    {
        VersionRange? profileRange = profileScope.Profile?.ExtensionBundleVersionRange;
        string? profileRangeText = SetupRuntimes.RangeText(profileRange);

        if (hostJsonBundle is null)
        {
            return SetupDependency.Bundle(BundleHelpers.StableBundleId, profileRange, profileRangeText, channel);
        }

        VersionRange? effectiveRange = VersionRangeIntersection.Intersect(hostJsonBundle.Version, profileRangeText);
        return effectiveRange is null
            ? null
            : SetupDependency.Bundle(hostJsonBundle.Id, effectiveRange, SetupRuntimes.RangeText(effectiveRange), channel);
    }
}
