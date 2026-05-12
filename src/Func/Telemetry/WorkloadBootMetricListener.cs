// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// Listens for <see cref="TelemetryConventions.WorkloadBootActivityName"/>
/// activity stops and translates each one into a sample on the boot-duration
/// histogram. Centralising the trace-to-metric bridge here keeps callers
/// emitting a single signal (the activity) and guarantees the metric carries
/// the same outcome and workload-count tags as the trace.
/// </summary>
/// <remarks>
/// Subscription is registered once in <see cref="Hosting.CliHost.CreateBuilder"/>
/// at process startup. The listener is process-global; for a short-lived CLI
/// process this is fine. Recording happens after the host has started, so
/// the OpenTelemetry meter pipeline is already subscribed and the sample
/// is exported.
/// </remarks>
internal static class WorkloadBootMetricListener
{
    private static int _registered;

    /// <summary>
    /// Idempotently subscribes the activity-to-metric bridge.
    /// </summary>
    public static void EnsureRegistered()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 1)
        {
            return;
        }

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => source.Name == CliTelemetry.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnActivityStopped,
        });
    }

    private static void OnActivityStopped(Activity activity)
    {
        if (!string.Equals(activity.OperationName, TelemetryConventions.WorkloadBootActivityName, StringComparison.Ordinal))
        {
            return;
        }

        int count = activity.GetTagItem(TelemetryConventions.CliWorkloadCount) is int value ? value : 0;
        string outcome = activity.Status == ActivityStatusCode.Error
            ? TelemetryConventions.OutcomeError
            : TelemetryConventions.OutcomeSuccess;

        CliTelemetry.Metric.RecordWorkloadBoot(count, (long)activity.Duration.TotalMilliseconds, outcome);
    }
}
