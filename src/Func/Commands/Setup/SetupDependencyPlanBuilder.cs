// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Setup;

internal interface ISetupDependencyPlanBuilder
{
    /// <summary>
    /// Expands a feature plan into the concrete dependencies to install for one
    /// profile scope, plus the failures detected while planning (unsupported
    /// runtime for the profile, unsupported worker RID, non-overlapping bundle
    /// ranges).
    /// </summary>
    public Task<SetupDependencyPlan> BuildDependencyPlanAsync(
        DirectoryInfo workingDirectory,
        SetupFeaturePlan featurePlan,
        SetupProfileScope profileScope,
        CancellationToken cancellationToken);
}

internal sealed class SetupDependencyPlanBuilder(
    IHostJsonBundleSectionReader hostJsonBundleSectionReader) : ISetupDependencyPlanBuilder
{
    private readonly IHostJsonBundleSectionReader _hostJsonBundleSectionReader = hostJsonBundleSectionReader ?? throw new ArgumentNullException(nameof(hostJsonBundleSectionReader));

    public async Task<SetupDependencyPlan> BuildDependencyPlanAsync(
        DirectoryInfo workingDirectory,
        SetupFeaturePlan featurePlan,
        SetupProfileScope profileScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);
        ArgumentNullException.ThrowIfNull(featurePlan);
        ArgumentNullException.ThrowIfNull(profileScope);

        List<SetupDependency> dependencies = [];
        List<SetupDependencyResult> failures = [];

        HostJsonBundleSection? hostJsonBundle = await _hostJsonBundleSectionReader.ReadAsync(workingDirectory, cancellationToken);
        BundleChannel bundleChannel = ResolveBundleChannel(hostJsonBundle);

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
                // Script stacks ship per-channel templates that track the bundle
                // channel; dotnet templates don't use bundles, so they stay channel-less.
                BundleChannel? templatesChannel = SetupRuntimes.IsDotNetRuntime(runtimeFeature.Name) ? null : bundleChannel;
                dependencies.Add(SetupDependency.Templates(runtimeFeature.Name, templatesChannel));
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
