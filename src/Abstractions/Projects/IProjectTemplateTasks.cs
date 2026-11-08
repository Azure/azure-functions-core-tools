// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// Interprets and performs supported template-defined tasks for a project.
/// </summary>
public interface IProjectTemplateTasks
{
    /// <summary>
    /// Validates a task without changing the project and returns whether execution is required.
    /// Unsupported or uncertain tasks must fail instead of returning false.
    /// </summary>
    public Task<bool> RequiresExecutionAsync(
        WorkingDirectory directory, Guid actionId, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken);

    /// <summary>
    /// Performs a task after its effects have been authorized.
    /// </summary>
    public Task ExecuteAsync(
        WorkingDirectory directory, Guid actionId, IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken);
}
