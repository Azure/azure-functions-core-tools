// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Confidence level a detector returns from <see cref="IProjectDetector.DetectAsync"/>.
/// </summary>
public enum DetectionConfidence
{
    /// <summary>The directory is not this detector's project.</summary>
    No,

    /// <summary>The detector sees plausible signal but cannot commit.</summary>
    Maybe,

    /// <summary>The detector claims the directory.</summary>
    Yes,
}
