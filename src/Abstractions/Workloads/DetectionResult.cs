// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Outcome of <see cref="IProjectDetector.DetectAsync"/>. <see cref="Reason"/>
/// is shown verbatim in verbose output and disambiguation messages so the
/// user can see why each detector answered the way it did.
/// </summary>
/// <param name="Confidence">The detector's verdict.</param>
/// <param name="Reason">Optional human-readable justification (e.g. <c>"matched *.csproj"</c>).</param>
public sealed record DetectionResult(DetectionConfidence Confidence, string? Reason = null)
{
    /// <summary>Convenience factory: detector does not claim the directory.</summary>
    public static DetectionResult No(string? reason = null) => new(DetectionConfidence.No, reason);

    /// <summary>Convenience factory: detector sees plausible signal but cannot commit.</summary>
    public static DetectionResult Maybe(string? reason = null) => new(DetectionConfidence.Maybe, reason);

    /// <summary>Convenience factory: detector confidently claims the directory.</summary>
    public static DetectionResult Yes(string? reason = null) => new(DetectionConfidence.Yes, reason);
}
