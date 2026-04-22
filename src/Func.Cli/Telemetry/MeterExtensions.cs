// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Extension members for emitting CLI metrics through <see cref="Meter"/>.
/// </summary>
/// <remarks>
/// Instrument names follow the (experimental) OTel CLI semantic conventions
/// (<c>cli.command.*</c>) and tag keys use OTel attribute names. When no
/// <see cref="MeterListener"/> is subscribed, the underlying instruments are
/// effectively no-ops, so callers do not need to branch on opt-out state.
/// </remarks>
public static class MeterExtensions
{
    private static readonly Counter<long> _commandCount =
        CliTelemetry.Meter.CreateCounter<long>(
            TelemetryConventions.CommandCountInstrument,
            unit: "{command}",
            description: "Number of CLI commands invoked.");

    private static readonly Histogram<double> _commandDuration =
        CliTelemetry.Meter.CreateHistogram<double>(
            TelemetryConventions.CommandDurationInstrument,
            unit: "ms",
            description: "Duration of CLI command execution.");

    extension(Meter meter)
    {
        /// <summary>
        /// Records a single command invocation: bumps the count and adds a
        /// duration sample. <paramref name="exitCode"/> follows process
        /// exit-code conventions (0 == success).
        /// </summary>
        /// <remarks>
        /// The instruments are bound to <see cref="CliTelemetry.Meter"/>; the
        /// receiver is required by the extension shape but is not otherwise
        /// inspected. Call as <c>CliTelemetry.Meter.RecordCommand(...)</c>.
        /// </remarks>
        public void RecordCommand(string commandName, int exitCode, long durationMs)
        {
            var tags = new TagList
            {
                { TelemetryConventions.CliCommandName, commandName },
                { TelemetryConventions.ProcessExitCode, exitCode },
            };

            _commandCount.Add(1, tags);
            _commandDuration.Record(durationMs, tags);
        }
    }
}
