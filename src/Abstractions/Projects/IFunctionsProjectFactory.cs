// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Creates a Functions project when a language stack recognizes the working directory.
/// </summary>
/// <remarks>
/// Capture the project inputs used for capability reporting once and return a new project instance for each observation.
/// Inspection must honor cancellation and must not refresh an existing project's reports.
/// </remarks>
public interface IFunctionsProjectFactory
{
    public Task<ProjectCreationResult> TryCreateProjectAsync(ProjectCreationContext context, CancellationToken cancellationToken);
}
