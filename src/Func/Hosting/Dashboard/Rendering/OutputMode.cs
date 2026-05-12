// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Hosting.Dashboard.Rendering;

/// <summary>
/// Output mode for <c>func start</c>. Selected explicitly via
/// <c>--output</c>, implicitly via <c>--no-tui</c>, or resolved
/// automatically from terminal capabilities.
/// </summary>
internal enum OutputMode
{
    /// <summary>Interactive TTY with a live-updating header.</summary>
    Compact,

    /// <summary>Streaming, ANSI-free, grep-friendly.</summary>
    Plain,

    /// <summary>NDJSON for programmatic consumers and AI agents.</summary>
    Json,
}
