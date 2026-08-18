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

        dependencies.Add(SetupDependency.Host(profileScope.Profile?.HostVersionRange));

        foreach (SetupRuntimeFeature feature in featurePlan.RuntimeFeatures)
        {
            // A package can publish several interchangeable aliases, but worker
            // ids, templates, and profile runtimes are all keyed off the primary
            // one. Fold before planning or an alternate spelling half-installs.
            SetupRuntimeFeature runtimeFeature = Canonicalize(feature, stacks);

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
                failures.Add(SetupDependencyResult.Failed(
                    SetupDependency.Runtime(runtimeFeature.Name),
                    $"More than one workload package on this feed claims the '{runtimeFeature.Name}' stack. "
                    + "Install it explicitly with 'func workload install <package-id>', "
                    + "or point --source at a feed that publishes it once."));
                continue;
            }

            if (runtimeFeature.InstallWorker)
            {
                VersionRange? workerRange = null;
                profileScope.Profile?.WorkerVersionRanges.TryGetValue(runtimeFeature.ProfileRuntime, out workerRange);
                dependencies.Add(SetupDependency.Worker(runtimeFeature.Name, workerRange));
            }

            if (stacks.StackPackageId(runtimeFeature.Name) is { } stackPackageId)
            {
                dependencies.Add(SetupDependency.Stack(runtimeFeature.Name, stackPackageId));
            }

            if (stacks.TemplatesPackageId(runtimeFeature.Name) is { } templatesPackageId)
            {
                // Script stacks ship per-channel templates that track the bundle
                // channel; dotnet templates don't use bundles, so they stay channel-less.
                BundleChannel? templatesChannel = SetupRuntimes.IsDotNetRuntime(runtimeFeature.Name) ? null : bundleChannel;
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

    /// <summary>
    /// Rewrites an alternate stack alias to the primary name the package
    /// publishes it under. Leaves the feature alone when it is already primary,
    /// unknown, or contested, so those keep their existing failure paths.
    /// </summary>
    private static SetupRuntimeFeature Canonicalize(SetupRuntimeFeature feature, SetupStackSnapshot stacks)
    {
        string canonical = stacks.CanonicalStackName(feature.Name);
        if (string.Equals(canonical, feature.Name, StringComparison.OrdinalIgnoreCase))
        {
            return feature;
        }

        // Runtimes whose profile name is deliberately different from the feature
        // name (dotnet) never carry a secondary alias, so this only ever moves
        // the pair that the feature resolver set to the same value.
        string profileRuntime = string.Equals(feature.ProfileRuntime, feature.Name, StringComparison.OrdinalIgnoreCase)
            ? canonical
            : feature.ProfileRuntime;

        return feature with { Name = canonical, ProfileRuntime = profileRuntime };
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
