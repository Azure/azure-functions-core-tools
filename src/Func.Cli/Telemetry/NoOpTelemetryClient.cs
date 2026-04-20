// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// No-op telemetry client used when telemetry is disabled.
/// Avoids creating any OTel infrastructure.
/// </summary>
public sealed class NoOpTelemetryClient : ITelemetry
{
    public bool IsEnabled => false;

    public void TrackCommand(string commandName, bool isSuccess, long durationMs, IDictionary<string, string>? properties = null) { }
    public void TrackException(Exception exception) { }
    public void Flush() { }
}
