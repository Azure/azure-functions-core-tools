// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Builds worker workload package identifiers and install commands.
/// </summary>
internal static class FunctionsWorkerWorkloadPackages
{
    private const string PackageIdPrefix = "Azure.Functions.Cli.Workloads.Workers.";

    public static string GetPackageId(FunctionsWorkerId workerId)
    {
        ArgumentNullException.ThrowIfNull(workerId);

        return PackageIdPrefix + workerId.Value;
    }

    public static string GetInstallCommand(FunctionsWorkerId workerId)
        => $"func workload install {GetPackageId(workerId)} --exact";

    public static string GetRepairCommand(FunctionsWorkerId workerId)
        => $"{GetInstallCommand(workerId)} --force";
}
