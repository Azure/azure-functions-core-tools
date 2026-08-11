// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Commands.Setup;

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
    bool Optional = false,
    BundleChannel? Channel = null)
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

    public static IReadOnlyList<string> Stacks => [.. _stacks];

    // Workload search alias for the channel-fallback hint. Only the channeled
    // dependency kinds (bundle, templates) need one.
    public string? SearchAlias => Kind switch
    {
        SetupDependencyKind.ExtensionBundle => "bundles",
        SetupDependencyKind.Templates => $"{Name}-templates",
        _ => null,
    };

    public static SetupDependency Host(VersionRange? versionRange)
        => new(
            SetupDependencyKind.Host,
            "host",
            "host",
            HostWorkloadPackage.CurrentPackageId,
            versionRange,
            SetupRuntimes.RangeText(versionRange),
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
            WorkerPackageId(runtime),
            versionRange,
            SetupRuntimes.RangeText(versionRange),
            ResolvedPackageId: null,
            Optional: true);

    public static SetupDependency Bundle(string bundleId, VersionRange? versionRange, string? rangeText, BundleChannel channel)
        => new(
            SetupDependencyKind.ExtensionBundle,
            bundleId,
            "extension bundle",
            IInstalledBundleWorkloads.BundleWorkloadPackageId,
            versionRange,
            rangeText,
            ResolvedPackageId: null,
            Channel: channel);

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

    public static SetupDependency Templates(string stack, BundleChannel? channel)
        => new(
            SetupDependencyKind.Templates,
            stack,
            $"{stack} templates",
            TemplatesPackagePrefix + StackPackageSuffix(stack),
            VersionRange: null,
            RangeText: null,
            ResolvedPackageId: null,
            Optional: true,
            Channel: channel);

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

    private static string WorkerPackageId(string runtime)
        => string.Equals(runtime, "python", StringComparison.OrdinalIgnoreCase)
            ? PythonWorkerWorkloadPackage.CurrentPackageId
            : WorkerPackagePrefix + runtime;
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
    string Message,
    string? Warning = null)
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
