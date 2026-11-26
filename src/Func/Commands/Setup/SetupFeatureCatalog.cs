// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Workloads;
using Azure.Functions.Cli.Workloads.Catalog;
using Azure.Functions.Cli.Workloads.Storage;

namespace Azure.Functions.Cli.Commands.Setup;

internal static class SetupFeatureCatalog
{
    public const string RuntimeFeature = "runtime";
    public const string HostFeature = "host";

    public static SetupRuntimeFeature? GetStackRecipe(StackPackage package)
    {
        string id = package.Stack.Identifier;
        if (!SetupDependency.SupportsStack(id))
        {
            return null;
        }

        // The .NET recipe has a project-provided worker and no bundle. All other
        // recipes use the CLI ID, not the host spelling, for their profile worker key.
        bool installWorker = !string.Equals(id, "dotnet", StringComparison.OrdinalIgnoreCase);
        return new SetupRuntimeFeature(id, installWorker ? id : package.Stack.WorkerRuntime, installWorker, package);
    }

    public static bool IsUnsupportedLegacyFeature(string name)
        => string.Equals(name, ".net", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "dotnet-inprocess", StringComparison.OrdinalIgnoreCase);

    public static string? GetFeatureForInstalledPackage(WorkloadEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Stack is { } stack)
        {
            return SetupDependency.SupportsStack(stack.Identifier) ? stack.Identifier : null;
        }

        string packageId = entry.LogicalPackage?.PackageId ?? entry.PackageId;
        if (string.Equals(packageId, HostWorkloadPackage.PackageId, StringComparison.OrdinalIgnoreCase)
            || (entry.RuntimeIdentifier is { Length: > 0 } rid && string.Equals(packageId, HostWorkloadPackage.FromRuntimeIdentifier(rid), StringComparison.OrdinalIgnoreCase)))
        {
            return HostFeature;
        }

        return string.Equals(packageId, IInstalledBundleWorkloads.BundleWorkloadPackageId, StringComparison.OrdinalIgnoreCase) ? RuntimeFeature : null;
    }
}
