// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Resolves the Functions project for a command invocation.
/// </summary>
internal interface IFunctionsProjectResolver
{
    public Task<ProjectResolutionResult> ResolveProjectAsync(ProjectResolutionContext context, CancellationToken cancellationToken);
}
