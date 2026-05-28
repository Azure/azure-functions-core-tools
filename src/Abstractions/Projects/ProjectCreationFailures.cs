// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Creates <see cref="ProjectCreationFailure"/> instances.
/// </summary>
public static class ProjectCreationFailures
{
    public static ProjectCreationFailure WorkerNotResolved(FunctionsWorkerResolutionFailure workerFailure, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(workerFailure);
        return new ProjectCreationFailure.WorkerNotResolved(workerFailure, message ?? workerFailure.Message);
    }
}
