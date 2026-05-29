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
    /// invocation, or surfaces the muted breadcrumb hint when the user
    /// has already opted out. Returns <c>null</c> when the caller should
    /// continue with the user's command, or an exit code when the
    /// coordinator handled the invocation itself (currently only used for
    /// the post-setup "re-run `func init`" short-circuit).
    /// </summary>
    public Task<int?> EnsureFirstRunPromptedAsync(string commandName, ParseResult parseResult, CancellationToken cancellationToken);
}
