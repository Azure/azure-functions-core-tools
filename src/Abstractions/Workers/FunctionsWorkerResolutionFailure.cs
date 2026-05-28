// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workers;

/// <summary>
/// Describes why a Functions worker runtime could not be resolved.
/// </summary>
public abstract record FunctionsWorkerResolutionFailure
{
    private FunctionsWorkerResolutionFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Message = message;
    }

    public string Message { get; }

    public sealed record MissingCompatibleVersion(FunctionsWorkerId WorkerId, string? VersionConstraint, string Message)
        : FunctionsWorkerResolutionFailure(Message);

    public sealed record NotInstalled(FunctionsWorkerId WorkerId, string Message)
        : FunctionsWorkerResolutionFailure(Message);

    public sealed record InvalidInstallation(
        FunctionsWorkerId WorkerId,
        string PackageId,
        string PackageVersion,
        string WorkerConfigPath,
        string Message)
        : FunctionsWorkerResolutionFailure(Message);
}
