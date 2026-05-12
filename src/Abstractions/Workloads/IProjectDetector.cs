// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Decides whether a directory is "this workload's" project. Registered by a
/// workload from <see cref="Workload.Configure"/> via
/// <see cref="FunctionsCliBuilder.RegisterDetector"/> and consumed by built-in
/// commands that bind to a workload from a directory (init, new, pack, start).
///
/// Public so workload authors can implement it.
/// </summary>
public interface IProjectDetector
{
    /// <summary>
    /// Globs (relative to the working directory) the CLI evaluates as a
    /// pre-filter before invoking <see cref="DetectAsync"/>. If at least one
    /// glob matches a file under the working directory, the detector runs;
    /// if none match, it is skipped. An empty list means the detector is
    /// always a candidate (used by detectors whose decision is purely
    /// content- or runtime-based).
    /// </summary>
    public IReadOnlyList<string> ProjectMarkers { get; }

    /// <summary>
    /// Worker-runtime ids this detector claims (e.g. <c>"dotnet-isolated"</c>,
    /// <c>"python"</c>). Used to short-circuit resolution when
    /// <c>FUNCTIONS_WORKER_RUNTIME</c> is set in <c>local.settings.json</c>.
    /// Empty means the detector does not claim any runtime id and is reached
    /// only via the marker + <see cref="DetectAsync"/> path.
    /// </summary>
    public IReadOnlyList<string> WorkerRuntimes { get; }

    /// <summary>
    /// Inspects the directory and returns a confidence verdict. Invoked only
    /// after the <see cref="ProjectMarkers"/> pre-filter has matched (or when
    /// <see cref="ProjectMarkers"/> is empty).
    /// </summary>
    public Task<DetectionResult> DetectAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken);
}
