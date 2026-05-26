// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Result of resolving a Functions project for a command invocation.
/// </summary>
internal abstract record ProjectResolutionResult
{
    private ProjectResolutionResult()
    {
    }

    public sealed record Resolved(FunctionsProject Project, string Message) : ProjectResolutionResult;

    public sealed record NotResolved(string Message, ProjectCreationFailure? Failure = null) : ProjectResolutionResult;
}
