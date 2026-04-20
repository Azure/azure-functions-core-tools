// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Holds the singleton <see cref="ActivitySource"/>, <see cref="Meter"/>,
/// and metric instruments used to emit CLI telemetry.
/// </summary>
/// <remarks>
/// No OpenTelemetry SDK is wired up here. <c>Program.cs</c> conditionally
/// attaches the Azure Monitor exporter at startup; when it doesn't,
/// <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns
/// <c>null</c> and metric instruments record nothing — so the call sites
/// stay free of opt-out branches.
/// </remarks>
public static class CliTelemetry
{
    public const string SourceName = "Azure.Functions.Cli";

    /// <summary>
    /// Sentinel value used in dev/local builds where the real instrumentation
    /// key has not been injected via the build-time attribute.
    /// </summary>
    private const string EmptyInstrumentationKey = "00000000-0000-0000-0000-000000000000";

    public static readonly ActivitySource Source = new(SourceName, ActivityExtensions.CliVersion);

    public static readonly Meter Meter = new(SourceName, ActivityExtensions.CliVersion);

    private static readonly Counter<long> _commandCounter =
        Meter.CreateCounter<long>("func.cli.command.count", unit: "{command}", description: "Number of CLI commands invoked.");

    private static readonly Histogram<double> _commandDuration =
        Meter.CreateHistogram<double>("func.cli.command.duration", unit: "ms", description: "Duration of CLI command execution.");

    /// <summary>
    /// True when this build has a real instrumentation key and the user has
    /// not opted out. <c>Program.cs</c> uses this to decide whether to wire
    /// up the OpenTelemetry SDK.
    /// </summary>
    public static bool IsConfigured => GetConnectionString() is not null;

    /// <summary>
    /// Returns the Azure Monitor connection string when telemetry is
    /// configured and not opted out, otherwise <c>null</c>.
    /// </summary>
    public static string? GetConnectionString()
    {
        var key = Constants.TelemetryInstrumentationKey;
        if (string.IsNullOrEmpty(key) || key == EmptyInstrumentationKey)
        {
            return null;
        }

        var optOut = Environment.GetEnvironmentVariable(Constants.TelemetryOptOutEnvVar);
        if (!string.IsNullOrEmpty(optOut) &&
            !(optOut.Equals("0", StringComparison.OrdinalIgnoreCase) ||
              optOut.Equals("false", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return $"InstrumentationKey={key}";
    }

    /// <summary>
    /// Records a command-execution metric (count + duration histogram).
    /// No-op when no <see cref="MeterListener"/> is subscribed.
    /// </summary>
    public static void TrackCommand(string commandName, bool isSuccess, long durationMs, IDictionary<string, string>? properties = null)
    {
        var tags = new TagList
        {
            { "command.name", commandName },
            { "command.success", isSuccess },
            { "cli.version", ActivityExtensions.CliVersion },
        };

        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                tags.Add($"command.{key}", value);
            }
        }

        _commandCounter.Add(1, tags);
        _commandDuration.Record(durationMs, tags);
    }
}
