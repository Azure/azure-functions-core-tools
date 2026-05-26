// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace Azure.Functions.Cli.Workloads;

internal static class HostWorkloadPackage
{
    private const string PackageIdPrefix = "Azure.Functions.Cli.Workloads.Host.";

    public static string CurrentPackageId => FromRuntimeIdentifier(CurrentRuntimeIdentifier);

    public static string CurrentRuntimeIdentifier
    {
        get
        {
            string runtimeIdentifier = RuntimeInformation.RuntimeIdentifier;
            if (string.IsNullOrWhiteSpace(runtimeIdentifier))
            {
                throw new InvalidOperationException("Unable to determine the current runtime identifier for the host workload package.");
            }

            return runtimeIdentifier.Trim();
        }
    }

    public static string FromRuntimeIdentifier(string runtimeIdentifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        return PackageIdPrefix + runtimeIdentifier.Trim().ToLowerInvariant();
    }
}
