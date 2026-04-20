// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Telemetry;
using Xunit;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class TelemetryTests
{
    [Fact]
    public void GetConnectionString_DefaultBuild_IsNull()
    {
        // Default build has the all-zeros instrumentation key, so telemetry
        // is not configured.
        Assert.Null(CliTelemetry.GetConnectionString());
        Assert.False(CliTelemetry.IsConfigured);
    }

    [Fact]
    public void StartCommandActivity_NoListener_ReturnsNull()
    {
        // Without an OTel listener subscribed, ActivitySource.StartActivity
        // returns null and the extension propagates that.
        var activity = CliTelemetry.Source.StartCommandActivity("test");
        Assert.Null(activity);
    }

    [Fact]
    public void TrackCommand_NoListener_DoesNotThrow()
    {
        // Metric instruments record nothing when no MeterListener is wired up.
        CliTelemetry.TrackCommand("test", isSuccess: true, durationMs: 100);
        CliTelemetry.TrackCommand(
            "test",
            isSuccess: false,
            durationMs: 50,
            properties: new Dictionary<string, string> { ["key"] = "value" });
    }

    [Fact]
    public void SetCommandTags_AppliesExpectedTags()
    {
        using var source = new ActivitySource("Test.Source");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Test.Source",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test");
        Assert.NotNull(activity);

        activity.SetCommandTags("workload install");

        Assert.Equal("workload install", activity.GetTagItem("command.name"));
        Assert.NotNull(activity.GetTagItem("cli.version"));
        Assert.NotNull(activity.GetTagItem("os.type"));
    }

    [Fact]
    public void SetCommandResult_Success_SetsOk()
    {
        using var source = new ActivitySource("Test.Source.Result");
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Test.Source.Result",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test");
        Assert.NotNull(activity);

        activity.SetCommandResult(isSuccess: true);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);

        activity.SetCommandResult(isSuccess: false, "boom");
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("boom", activity.StatusDescription);
    }
}
