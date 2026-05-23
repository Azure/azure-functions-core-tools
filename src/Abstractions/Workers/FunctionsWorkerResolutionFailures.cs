// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Creates <see cref="FunctionsWorkerResolutionFailure"/> instances.
/// </summary>
public static class FunctionsWorkerResolutionFailures
{
    public static FunctionsWorkerResolutionFailure MissingCompatibleVersion(
        FunctionsWorkerId workerId,
        string? versionConstraint,
        string message)
        => new FunctionsWorkerResolutionFailure.MissingCompatibleVersion(workerId, versionConstraint, message);

    public static FunctionsWorkerResolutionFailure NotInstalled(FunctionsWorkerId workerId, string message)
        => new FunctionsWorkerResolutionFailure.NotInstalled(workerId, message);

    public static FunctionsWorkerResolutionFailure InvalidInstallation(
        FunctionsWorkerId workerId,
        string packageId,
        string packageVersion,
        string workerConfigPath,
        string message)
        => new FunctionsWorkerResolutionFailure.InvalidInstallation(
            workerId,
            packageId,
            packageVersion,
            workerConfigPath,
            message);
}
