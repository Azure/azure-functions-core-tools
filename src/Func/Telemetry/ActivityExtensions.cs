// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Extension members for working with <see cref="ActivitySource"/> /
/// <see cref="Activity"/> from CLI code.
/// </summary>
/// <remarks>
/// Naming and span shape follow the (experimental) OTel CLI semantic
/// conventions: <see cref="ActivityKind.Internal"/> kind, fixed operation
/// name <c>"cli.command"</c>, and the <c>cli.command.name</c> attribute
/// applied via <see cref="SetCommandName"/> once parsing resolves the
/// invoked command path. Common dimensions are added by
/// <see cref="CliActivityEnrichmentProcessor"/>.
/// </remarks>
internal static class ActivityExtensions
{
    /// <summary>
    /// Fixed operation name for CLI command spans. The user-facing command
    /// path is attached as a tag and (when known) as <c>DisplayName</c>.
    /// </summary>
    public const string CommandActivityName = "cli.command";

    extension(ActivitySource source)
    {
        /// <summary>
        /// Starts an <see cref="Activity"/> that represents the execution of
        /// a CLI command. Returns <c>null</c> when no listener is subscribed.
        /// Call <see cref="SetCommandName"/> once parsing resolves the
        /// invoked command path.
        /// </summary>
        public Activity? StartCommandActivity(DateTimeOffset startTime = default)
        {
            return source.StartActivity(CommandActivityName, ActivityKind.Internal, default(ActivityContext), startTime: startTime);
        }

        /// <summary>
        /// Emits the measured boot span after the telemetry provider has started.
        /// </summary>
        public void RecordWorkloadBootActivity(WorkloadBootTelemetry telemetry)
        {
            ArgumentNullException.ThrowIfNull(telemetry);

            using Activity? activity = source.StartActivity(
                TelemetryConventions.WorkloadBootActivityName,
                ActivityKind.Internal,
                default(ActivityContext),
                startTime: telemetry.StartTime);
            activity?.SetTag(TelemetryConventions.CliWorkloadCount, telemetry.WorkloadCount);
            activity?.SetEndTime((telemetry.StartTime + telemetry.Duration).UtcDateTime);
        }
    }

    extension(Activity activity)
    {
        /// <summary>
        /// Tags the activity with the resolved CLI command path and sets the
        /// display name so trace explorers show <c>"workload list"</c> rather
        /// than the operation name <c>"cli.command"</c>. Safe to call
        /// multiple times; the most recent value wins.
        /// </summary>
        public Activity SetCommandName(string commandName)
        {
            activity.DisplayName = commandName;
            activity.SetTag(TelemetryConventions.CliCommandName, commandName);
            return activity;
        }

        /// <summary>
        /// Records the exception on the activity and marks its status as
        /// <see cref="ActivityStatusCode.Error"/>. Sets the OTel
        /// <see cref="TelemetryConventions.ErrorType"/> attribute so the
        /// failure kind is queryable on the span (and on any metric a
        /// listener derives from it). Use this on the failure path; success
        /// is the default and does not need to be set.
        /// </summary>
        public Activity Fail(Exception exception)
        {
            activity.AddException(exception);
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.SetTag(TelemetryConventions.ErrorType, exception.GetType().FullName);
            return activity;
        }
    }
}
