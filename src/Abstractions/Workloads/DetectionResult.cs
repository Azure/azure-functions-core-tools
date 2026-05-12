// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Outcome of <see cref="IProjectDetector.DetectAsync"/>. <see cref="Reason"/>
/// is shown verbatim in verbose output and disambiguation messages so the
/// user can see why each detector answered the way it did.
/// </summary>
/// <param name="Claimed"><c>true</c> if the detector claims the directory.</param>
/// <param name="Reason">Optional human-readable justification (e.g. <c>"matched *.csproj"</c>).</param>
public sealed record DetectionResult(bool Claimed, string? Reason = null)
{
    /// <summary>Convenience factory: detector does not claim the directory.</summary>
    public static DetectionResult No(string? reason = null) => new(false, reason);

    /// <summary>Convenience factory: detector claims the directory.</summary>
    public static DetectionResult Yes(string? reason = null) => new(true, reason);
}
