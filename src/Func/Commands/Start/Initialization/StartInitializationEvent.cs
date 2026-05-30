// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Commands.Start.Initialization;

/// <summary>
/// Base type for events emitted during startup initialization.
/// </summary>
internal abstract record StartInitializationEvent(DateTimeOffset Timestamp);

/// <summary>
/// Emitted before the first startup initialization step begins.
/// </summary>
internal sealed record StartInitializationStartedEvent(DateTimeOffset Timestamp, string ProfileName)
    : StartInitializationEvent(Timestamp);

/// <summary>
/// Emitted when an initialization step begins.
/// </summary>
internal sealed record StartInitializationStepStartedEvent(DateTimeOffset Timestamp, StartInitializationStep Step)
    : StartInitializationEvent(Timestamp);

/// <summary>
/// Emitted when a progress-based initialization step advances.
/// </summary>
internal sealed record StartInitializationProgressEvent(DateTimeOffset Timestamp, string StepId, double Percent, string? Message = null)
    : StartInitializationEvent(Timestamp);

/// <summary>
/// Emitted when an initialization step streams log output.
/// </summary>
internal sealed record StartInitializationLogEvent(DateTimeOffset Timestamp, string StepId, string Line, FunctionsProjectReportSeverity Severity)
    : StartInitializationEvent(Timestamp);

/// <summary>
/// Emitted when an initialization step completes successfully.
/// </summary>
internal sealed record StartInitializationStepCompletedEvent(DateTimeOffset Timestamp, string StepId, string? Message = null)
    : StartInitializationEvent(Timestamp);

/// <summary>
/// Emitted when an initialization step fails before the exception propagates.
/// </summary>
internal sealed record StartInitializationStepFailedEvent(DateTimeOffset Timestamp, string StepId, string? Message = null)
    : StartInitializationEvent(Timestamp);

/// <summary>
/// Emitted when initialization completes and the dashboard event stream can start.
/// </summary>
internal sealed record StartInitializationCompletedEvent(DateTimeOffset Timestamp, StartInitializationResult Result)
    : StartInitializationEvent(Timestamp);
