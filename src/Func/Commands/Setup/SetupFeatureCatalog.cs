// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Setup;

// Maps workload package ids and worker runtimes to the matching
// `func setup --features <name>` value, so the bundle resolver and the
// workload install command can point users at the higher-level setup
// command using a single source of truth.
internal static class SetupFeatureCatalog
{
    public const string RuntimeFeature = "runtime";
    public const string HostFeature = "host";

    private const string WorkerPackagePrefix = "Azure.Functions.Cli.Workloads.Workers.";
    private const string TemplatesPackagePrefix = "Azure.Functions.Cli.Workloads.Templates.";
    private const string StackPackagePrefix = "Azure.Functions.Cli.Workloads.";

    private static readonly HashSet<string> _supportedStacks =
        new(StringComparer.OrdinalIgnoreCase) { "dotnet", "node", "python", "go" };

    public static bool TryGetFeatureForRuntime(string? workerRuntime, out string feature)
    {
        if (!string.IsNullOrWhiteSpace(workerRuntime))
        {
            string normalized = workerRuntime.Trim().ToLowerInvariant();
            if (_supportedStacks.Contains(normalized))
            {
                feature = normalized;
                return true;
            }
        }

        feature = string.Empty;
        return false;
    }

    public static bool TryGetFeatureForPackageId(string? packageId, out string feature)
    {
        feature = string.Empty;
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        string id = packageId.Trim();

        if (id.StartsWith(HostWorkloadPackage.PackageIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            feature = HostFeature;
            return true;
        }

        if (id.StartsWith(PythonWorkerWorkloadPackage.PackageIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            feature = "python";
            return true;
        }

        if (string.Equals(id, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase))
        {
            feature = RuntimeFeature;
            return true;
        }

        if (TryGetStackFeatureFromPrefix(id, WorkerPackagePrefix, out feature)
            || TryGetStackFeatureFromPrefix(id, TemplatesPackagePrefix, out feature)
            // Stack prefix is also a prefix of worker/templates ids, so it must run last.
            || TryGetStackFeatureFromPrefix(id, StackPackagePrefix, out feature))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetStackFeatureFromPrefix(string id, string prefix, out string feature)
    {
        feature = string.Empty;
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || id.Length <= prefix.Length)
        {
            return false;
        }

        string suffix = id[prefix.Length..];
        if (suffix.Contains('.'))
        {
            return false;
        }

        string normalized = suffix.ToLowerInvariant();
        if (_supportedStacks.Contains(normalized))
        {
            feature = normalized;
            return true;
        }

        return false;
    }
}
