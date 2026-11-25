// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Whether a template's 'Run script' post actions may execute arbitrary commands.
/// </summary>
/// <remarks>
/// This is a consent policy, not a sandbox: an allowed script runs with the user's full permissions.
/// </remarks>
internal enum AllowRunScripts
{
    /// <summary>
    /// Never run scripts; report them as declined with manual instructions.
    /// </summary>
    No,

    /// <summary>
    /// Run scripts without asking.
    /// </summary>
    Yes,

    /// <summary>
    /// Ask before running each script, declining when input is unavailable.
    /// </summary>
    Prompt,
}
