// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;

namespace Azure.Functions.Cli.Hosting.FirstRun;

/// <summary>
/// Orchestrates the one-time first-run prompt that offers to run
/// <c>func setup</c> before the user's command executes.
/// </summary>
internal interface IFirstRunCoordinator
{
    /// <summary>
    /// Offers to run setup when this looks like the user's first
    /// invocation. Always returns once the prompt has been handled
    /// (or skipped); the caller continues with the user's command.
    /// </summary>
    public Task EnsureFirstRunPromptedAsync(string commandName, ParseResult parseResult, CancellationToken cancellationToken);
}
