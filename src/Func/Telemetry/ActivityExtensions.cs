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
/// conventions: <see cref="ActivityKind.Internal"/> kind, span name set to
/// the subcommand path, and the <c>cli.command.name</c> attribute.
/// Resource-level attributes (<c>service.name</c>, <c>service.version</c>,
/// <c>os.*</c>, <c>process.runtime.*</c>) are configured once on the
/// <see cref="OpenTelemetry.Resources.ResourceBuilder"/> and inherited by
/// every span — they should not be set per span here.
/// </remarks>
internal static class ActivityExtensions
{
    extension(ActivitySource source)
    {
        /// <summary>
        /// Starts an <see cref="Activity"/> that represents the execution of
        /// a CLI command. Returns <c>null</c> when no listener is subscribed.
        /// </summary>
        public Activity? StartCommandActivity(string commandName)
        {
            Activity? activity = source.StartActivity(commandName, ActivityKind.Internal);
            activity?.SetTag(TelemetryConventions.CliCommandName, commandName);
            return activity;
        }

        /// <summary>
        /// Starts an <see cref="Activity"/> that represents the workload load
        /// + Configure phase at CLI startup. The activity is also picked up
        /// by <see cref="WorkloadBootMetricListener"/>, which translates its
        /// stop into the boot-duration metric so trace and metric stay in
        /// sync. Returns <c>null</c> when no listener is subscribed.
        /// </summary>
        public Activity? StartWorkloadBootActivity()
            => source.StartActivity(TelemetryConventions.WorkloadBootActivityName, ActivityKind.Internal);
    }

    extension(Activity activity)
    {
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
