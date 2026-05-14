// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Outcome of <see cref="IProjectDetector.DetectAsync"/>. <see cref="Reason"/>
/// shows up verbatim in disambiguation messages.
/// </summary>
/// <param name="Claimed"><c>true</c> if the detector claims the directory.</param>
/// <param name="Reason">Optional human-readable justification (e.g. <c>"matched *.csproj"</c>).</param>
/// <param name="WorkerRuntime">
/// Optional <c>FUNCTIONS_WORKER_RUNTIME</c> id this detector claims for the
/// directory. Used as a tie-breaker when <c>local.settings.json</c> sets one.
/// </param>
public sealed record DetectionResult(bool Claimed, string? Reason = null, string? WorkerRuntime = null)
{
    public static DetectionResult No(string? reason = null) => new(false, reason);

    public static DetectionResult Yes(string? reason = null, string? workerRuntime = null) =>
        new(true, reason, workerRuntime);
}
