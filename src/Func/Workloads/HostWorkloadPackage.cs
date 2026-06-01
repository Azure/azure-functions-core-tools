// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

internal static class HostWorkloadPackage
{
    internal const string PackageIdPrefix = "Azure.Functions.Cli.Workloads.Host.";

    public static string CurrentPackageId => FromRuntimeIdentifier(CurrentRuntimeIdentifier);

    public static string CurrentRuntimeIdentifier => WorkloadRuntimeIdentifier.Current;

    public static string FromRuntimeIdentifier(string runtimeIdentifier)
        => WorkloadRuntimeIdentifier.Qualify(PackageIdPrefix, runtimeIdentifier);
}
