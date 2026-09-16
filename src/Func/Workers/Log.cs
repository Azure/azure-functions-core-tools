// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Source-generated log messages for worker resolution diagnostics.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message =
        "[worker-resolve] No workloads found for worker '{WorkerId}'. Searched package ID '{PackageId}' and alias '{Alias}'. "
        + "Provider has {TotalCount} content workload(s): [{AllWorkloads}]")]
    public static partial void WorkloadSearchEmpty(
        ILogger logger, string workerId, string packageId, string alias, int totalCount, string allWorkloads);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "[worker-resolve] No installed workloads matched for worker '{WorkerId}'. "
        + "The worker may be present on disk but not registered with the workload provider.")]
    public static partial void NoInstalledWorkloadsMatched(ILogger logger, string workerId);

    [LoggerMessage(Level = LogLevel.Debug, Message =
        "[worker-resolve] Skipping workload '{PackageId}': version '{PackageVersion}' is not a valid NuGet version.")]
    public static partial void SkippingInvalidVersion(ILogger logger, string packageId, string packageVersion);

    [LoggerMessage(Level = LogLevel.Debug, Message =
        "[worker-resolve] Skipping workload '{PackageId}' version '{PackageVersion}': does not satisfy constraint '{Constraint}'.")]
    public static partial void SkippingConstraintMismatch(ILogger logger, string packageId, string packageVersion, string constraint);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "[worker-resolve] No installed workloads for '{WorkerId}' satisfy version constraint '{Constraint}'. Installed: [{Packages}]")]
    public static partial void NoCompatibleVersion(ILogger logger, string workerId, string constraint, string packages);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "[worker-resolve] Selected workload '{PackageId}' version '{Version}' is missing worker config at '{ConfigPath}'.")]
    public static partial void MissingWorkerConfig(ILogger logger, string packageId, string version, string configPath);

    [LoggerMessage(Level = LogLevel.Debug, Message =
        "[worker-resolve] Initial resolution failed ({FailureType}): {Message}. Attempting install for '{WorkerId}'.")]
    public static partial void ResolutionFailedAttemptingInstall(ILogger logger, string failureType, string message, string workerId);

    [LoggerMessage(Level = LogLevel.Warning, Message =
        "[worker-resolve] Worker resolution failed. Profile: '{ProfileName}', worker version ranges: [{Ranges}], failure ({FailureType}): {Message}")]
    public static partial void WorkerResolutionFailed(ILogger logger, string profileName, string ranges, string failureType, string message);
}
