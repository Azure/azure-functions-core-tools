// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads;

/// <summary>
/// Owner-aware wrapper around an <see cref="IProjectDetector"/> contributed by
/// a workload. Mirrors the ownership pattern <see cref="ExternalCommand"/> uses
/// for commands so the resolver can attribute its results back to the workload
/// that registered the detector.
///
/// Internal: workloads register a bare <see cref="IProjectDetector"/> through
/// <see cref="FunctionsCliBuilder.RegisterProjectDetector"/>; the host wraps it here.
/// </summary>
/// <param name="Workload">The workload that registered <paramref name="Detector"/>.</param>
/// <param name="Detector">The detector instance the workload supplied.</param>
internal sealed record WorkloadDetectorContribution(WorkloadInfo Workload, IProjectDetector Detector);
