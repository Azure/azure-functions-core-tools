// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// How <see cref="IQuickstartScaffolder"/> should fetch the template payload.
/// </summary>
internal enum FetchMode
{
    /// <summary>
    /// Use <c>git</c> when available; otherwise fall back to <see cref="Http"/>.
    /// </summary>
    Auto,

    /// <summary>
    /// Shallow-clone the repository with <c>git</c>. Fails fast if git is not on PATH.
    /// </summary>
    Git,

    /// <summary>
    /// Download a zip archive from the GitHub archive endpoint.
    /// </summary>
    Http,
}
