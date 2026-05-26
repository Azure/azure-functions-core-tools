// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Workers;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Describes why a recognized Functions project could not be created.
/// </summary>
public abstract record ProjectCreationFailure
{
    private ProjectCreationFailure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Message = message;
    }

    public string Message { get; }

    public sealed record WorkerNotResolved(FunctionsWorkerResolutionFailure WorkerFailure, string Message)
        : ProjectCreationFailure(Message);
}
