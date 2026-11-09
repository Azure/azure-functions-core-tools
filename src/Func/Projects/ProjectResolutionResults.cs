// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Creates <see cref="ProjectResolutionResult"/> instances.
/// </summary>
internal static class ProjectResolutionResults
{
    public static ProjectResolutionResult Resolved(FunctionsProject project, string message)
        => new ProjectResolutionResult.Resolved(project, message);

    public static ProjectResolutionResult NotResolved(string message, ProjectCreationFailure? failure = null)
        => new ProjectResolutionResult.NotResolved(message, failure);

    public static ProjectResolutionResult Uninitialized()
        => new ProjectResolutionResult.Uninitialized(
            "This directory is not an initialized Azure Functions project. Run 'func init' to initialize a Functions project.");
}
