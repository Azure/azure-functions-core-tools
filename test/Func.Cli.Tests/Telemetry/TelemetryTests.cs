// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Telemetry;
using Xunit;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class TelemetryTests
{
    [Fact]
    public void TryGetConnectionString_DefaultBuild_ReturnsFalse()
    {
        // Default build has the all-zeros instrumentation key, so telemetry
        // is not configured.
        Assert.False(CliTelemetry.TryGetConnectionString(out var connectionString));
        Assert.Null(connectionString);
    }

    [Fact]
    public void StartCommandActivity_NoListener_ReturnsNull()
    {
        // Without an OTel listener subscribed, ActivitySource.StartActivity
        // returns null and the extension propagates that.
        Assert.Null(CliTelemetry.Trace.StartCommandActivity("test"));
    }

    [Fact]
    public void RecordCommand_NoListener_DoesNotThrow()
    {
        // Metric instruments record nothing when no MeterListener is wired up.
        CliTelemetry.Metric.RecordCommand("test", exitCode: 0, durationMs: 100);
        CliTelemetry.Metric.RecordCommand("test", exitCode: 1, durationMs: 50);
    }

    [Fact]
    public void StartCommandActivity_WithListener_AppliesCommandNameTag()
    {
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity("workload install");
        Assert.NotNull(activity);

        Assert.Equal("workload install", activity.GetTagItem("cli.command.name"));
        Assert.Equal(ActivityKind.Internal, activity.Kind);
    }

    [Fact]
    public void Fail_RecordsExceptionAndSetsErrorStatus()
    {
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity("test");
        Assert.NotNull(activity);

        var ex = new InvalidOperationException("boom");
        activity.Fail(ex);

        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("boom", activity.StatusDescription);
    }

    [Fact]
    public void CreateResourceBuilder_IncludesServiceAndOsAttributes()
    {
        var resource = CliTelemetry.CreateResourceBuilder().Build();
        var attrs = resource.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(CliTelemetry.SourceName, attrs["service.name"]);
        Assert.Equal(CliTelemetry.CliVersion, attrs["service.version"]);
        Assert.True(attrs.ContainsKey("os.type"));
        Assert.True(attrs.ContainsKey("os.architecture"));
        Assert.True(attrs.ContainsKey("process.runtime.description"));
    }

    private static ActivityListener SubscribeListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == CliTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
