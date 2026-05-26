// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Controls how template content is downloaded from GitHub.
/// </summary>
public enum FetchMode
{
    /// <summary>
    /// Probe for git availability; use git if found, otherwise fall back to HTTP.
    /// </summary>
    Auto,

    /// <summary>
    /// Use git clone. Fails if git is not available.
    /// </summary>
    Git,

    /// <summary>
    /// Use HTTP zip download. Never invokes git.
    /// </summary>
    Http
}
