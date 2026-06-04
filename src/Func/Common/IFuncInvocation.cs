// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Common;

/// <summary>
/// Single source of truth for the name v5 should suggest to the user in
/// command examples, plus alias-conflict diagnostics surfaced by the
/// preview nudge.
/// </summary>
/// <remarks>
/// <para>
/// At GA the CLI ships as <c>func</c> and detection is a no-op; today
/// (preview) the installer also drops a <c>func5</c> shim so v5 can run
/// alongside a v4 <c>func</c> that wins PATH. When that happens,
/// <see cref="RecommendedName"/> flips to <c>func5</c> so any caller that
/// renders the recommended invocation stays correct.
/// </para>
/// </remarks>
internal interface IFuncInvocation
{
    /// <summary>
    /// The executable name the CLI should tell the user to type
    /// (<c>"func"</c> by default, <c>"func5"</c> when an alias conflict
    /// has been detected).
    /// </summary>
    public string RecommendedName { get; }

    /// <summary>
    /// True when a <c>func</c> resolves on PATH but points at a different
    /// binary than the running process.
    /// </summary>
    public bool ConflictDetected { get; }

    /// <summary>
    /// Absolute path of the conflicting <c>func</c> on PATH, or
    /// <c>null</c> when <see cref="ConflictDetected"/> is <c>false</c>.
    /// </summary>
    public string? ConflictingPath { get; }
}
