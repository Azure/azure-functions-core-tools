// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Outcome of processing a template's post actions.
/// </summary>
[Flags]
internal enum PostActionExecutionStatus
{
    /// <summary>
    /// All post actions succeeded.
    /// </summary>
    Success = 0,

    /// <summary>
    /// One or more post actions failed or could not be processed.
    /// </summary>
    Failure = 1,

    /// <summary>
    /// One or more post actions were not run because running them was not allowed.
    /// </summary>
    Cancelled = 2,
}
