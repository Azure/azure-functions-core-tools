// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Resolves the absolute path of a <c>func</c> executable on the user's
/// <c>PATH</c>. Behind an interface so alias-conflict detection can be
/// exercised without touching the real environment.
/// </summary>
internal interface IFuncOnPathResolver
{
    /// <summary>
    /// Returns the absolute path of the first <c>func</c> executable found
    /// while walking <c>PATH</c> in order, or <c>null</c> when none is
    /// present. On Windows, candidates are matched against <c>PATHEXT</c>
    /// (so <c>func.cmd</c> from a side-by-side npm-installed v4 is found).
    /// On Unix the candidate must have the executable bit set.
    /// </summary>
    public string? ResolveFuncOnPath();
}
