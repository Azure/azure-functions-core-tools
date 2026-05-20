// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Result of a workload attempting to create a Functions project.
/// </summary>
public abstract record ProjectCreationResult
{
    private ProjectCreationResult()
    {
    }

    public sealed record Created(FunctionsProject Project, string Reason) : ProjectCreationResult;

    public sealed record NotCreated(string Reason) : ProjectCreationResult;

    public sealed record Failed(ProjectCreationFailure Failure) : ProjectCreationResult;
}
