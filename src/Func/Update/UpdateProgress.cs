// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Distinct phases the update pipeline moves through, in order. Used by
/// <see cref="UpdateProgress"/> so callers can drive a phase-aware UI
/// (spinner text, section headers, etc.) without coupling to internal
/// pipeline structure.
/// </summary>
internal enum UpdatePhase
{
    Downloading,
    Extracting,
    Installing,
    Verifying,
}

/// <summary>
/// Snapshot of update-pipeline progress reported through
/// <see cref="System.IProgress{T}"/>. <see cref="BytesRead"/> and
/// <see cref="TotalBytes"/> are populated only during the download phase and
/// only when the CDN response advertised a content length; other phases
/// leave them <see langword="null"/> and render as indeterminate progress.
/// </summary>
internal readonly record struct UpdateProgress(
    UpdatePhase Phase,
    long? BytesRead = null,
    long? TotalBytes = null);
