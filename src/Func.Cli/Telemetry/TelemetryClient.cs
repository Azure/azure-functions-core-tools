// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Azure.Functions.Cli.Telemetry;

/// <summary>
/// OpenTelemetry-based telemetry client that exports to Azure Monitor
/// via the Application Insights instrumentation key.
/// </summary>
public sealed class AppInsightsTelemetryClient : ITelemetry, IDisposable
{
    private const string SourceName = "Azure.Functions.Cli";
    private static readonly ActivitySource _activitySource = new(SourceName, GetCliVersion());

    private readonly TracerProvider? _tracerProvider;

    public bool IsEnabled { get; }

    public AppInsightsTelemetryClient()
    {
        IsEnabled = CheckEnabled();

        if (IsEnabled)
        {
            var connectionString = $"InstrumentationKey={Constants.TelemetryInstrumentationKey}";
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .AddSource(SourceName)
                .AddAzureMonitorTraceExporter(o => o.ConnectionString = connectionString)
                .Build();
        }
    }

    /// <summary>
    /// Starts a command activity span. Call this BEFORE command execution.
    /// Returns the activity so it can be completed after execution.
    /// </summary>
    public Activity? StartCommandActivity(string commandName)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var activity = _activitySource.StartActivity($"func {commandName}", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("command.name", commandName);
            AddCommonTags(activity);
        }

        return activity;
    }

    public void TrackCommand(string commandName, bool isSuccess, long durationMs, IDictionary<string, string>? properties = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        // If there's no current activity (e.g. quick commands), create one
        var activity = Activity.Current ?? _activitySource.StartActivity($"func {commandName}", ActivityKind.Client);
        if (activity is null)
        {
            return;
        }

        activity.SetTag("command.name", commandName);
        activity.SetTag("command.success", isSuccess);
        activity.SetTag("command.duration_ms", durationMs);
        AddCommonTags(activity);

        if (properties is not null)
        {
            foreach (var (key, value) in properties)
            {
                activity.SetTag($"command.{key}", value);
            }
        }

        activity.SetStatus(isSuccess ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

        // Stop the activity if we started it here
        if (activity != Activity.Current)
        {
            activity.Stop();
        }
    }

    public void TrackException(Exception exception)
    {
        if (!IsEnabled)
        {
            return;
        }

        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetStatus(ActivityStatusCode.Error, exception.Message);
            activity.AddException(exception);
        }
    }

    public void Flush()
    {
        _tracerProvider?.ForceFlush(3000);
    }

    public void Dispose()
    {
        _tracerProvider?.Dispose();
    }

    private static bool CheckEnabled()
    {
        var key = Constants.TelemetryInstrumentationKey;
        if (string.IsNullOrEmpty(key) || key == "00000000-0000-0000-0000-000000000000")
        {
            return false;
        }

        var optOut = Environment.GetEnvironmentVariable(Constants.TelemetryOptOutEnvVar);
        if (string.IsNullOrEmpty(optOut))
        {
            return true; // Enabled by default
        }

        return optOut.Equals("0", StringComparison.OrdinalIgnoreCase)
            || optOut.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCommonTags(Activity activity)
    {
        activity.SetTag("cli.version", GetCliVersion());
        activity.SetTag("os.type", RuntimeInformation.OSDescription);
        activity.SetTag("os.architecture", RuntimeInformation.OSArchitecture.ToString());
        activity.SetTag("runtime.framework", RuntimeInformation.FrameworkDescription);
    }

    private static string GetCliVersion()
    {
        return typeof(AppInsightsTelemetryClient).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
    }
}
