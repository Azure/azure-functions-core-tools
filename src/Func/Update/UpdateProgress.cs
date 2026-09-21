// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Update;

/// <summary>
/// Distinct phases of the update pipeline.
/// </summary>
internal enum UpdatePhase
{
    Downloading,
    Extracting,
    Installing,
    Verifying,
}

/// <summary>
/// Snapshot of update-pipeline progress.
/// </summary>
internal readonly record struct UpdateProgress(UpdatePhase Phase, long? BytesRead = null, long? TotalBytes = null);
