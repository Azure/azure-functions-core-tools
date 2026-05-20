// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Creates <see cref="ProjectResolutionResult"/> instances.
/// </summary>
internal static class ProjectResolutionResults
{
    public static ProjectResolutionResult Resolved(IFunctionsProject project, string message)
        => new ProjectResolutionResult.Resolved(project, message);

    public static ProjectResolutionResult NotResolved(string message, ProjectCreationFailure? failure = null)
        => new ProjectResolutionResult.NotResolved(message, failure);
}
