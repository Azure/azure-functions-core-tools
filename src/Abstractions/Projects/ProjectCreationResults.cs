// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Creates <see cref="ProjectCreationResult"/> instances.
/// </summary>
public static class ProjectCreationResults
{
    public static ProjectCreationResult Created(FunctionsProject project, string reason)
        => new ProjectCreationResult.Created(project, reason);

    public static ProjectCreationResult NotCreated(string reason)
        => new ProjectCreationResult.NotCreated(reason);

    public static ProjectCreationResult Failed(ProjectCreationFailure failure)
        => new ProjectCreationResult.Failed(failure);
}
