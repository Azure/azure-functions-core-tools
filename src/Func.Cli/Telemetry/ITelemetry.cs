// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Abstraction for telemetry tracking.
/// </summary>
public interface ITelemetry
{
    /// <summary>
    /// Tracks a CLI command execution.
    /// </summary>
    public void TrackCommand(string commandName, bool isSuccess, long durationMs, IDictionary<string, string>? properties = null);

    /// <summary>
    /// Tracks an exception.
    /// </summary>
    public void TrackException(Exception exception);

    /// <summary>
    /// Flushes any queued telemetry events.
    /// </summary>
    public void Flush();

    /// <summary>
    /// Whether telemetry is enabled.
    /// </summary>
    public bool IsEnabled { get; }
}
