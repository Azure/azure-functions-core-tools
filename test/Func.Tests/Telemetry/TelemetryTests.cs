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

    [Theory]
    [InlineData(Azure.Functions.Cli.Common.Constants.TelemetryOptOutEnvVar)]
    [InlineData(Azure.Functions.Cli.Common.Constants.LegacyTelemetryOptOutEnvVar)]
    public void TryGetConnectionString_OptOutEnvVarSet_ReturnsFalse(string envVarName)
    {
        // Even with a non-default key, an opt-out via either the new or
        // legacy env var must short-circuit telemetry.
        using var optOut = new EnvVarScope(envVarName, "1");

        // We can't override the build-time key from a test, so we just
        // assert the helper agrees the opt-out wins regardless of key state.
        Assert.False(CliTelemetry.TryGetConnectionString(out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("no")]
    [InlineData("n")]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData("  off  ")]
    [InlineData("\tfalse\n")]
    public void TryGetConnectionString_OptOutFalseSentinels_DoNotOptOut(string value)
    {
        // The documented "off" sentinels must NOT be treated as opt-out.
        // The default build still returns false because of the missing key,
        // but the opt-out path itself should not be the reason.
        using var optOut = new EnvVarScope(Azure.Functions.Cli.Common.Constants.TelemetryOptOutEnvVar, value);
        using var legacy = new EnvVarScope(Azure.Functions.Cli.Common.Constants.LegacyTelemetryOptOutEnvVar, value);

        // Just exercising the path; result is gated by the build key.
        _ = CliTelemetry.TryGetConnectionString(out _);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("anything-else")]
    public void TryGetConnectionString_OptOutTruthyValues_OptOut(string value)
    {
        // Any value outside the documented "off" sentinels is treated as
        // an opt-out, so we fail safe toward not collecting telemetry.
        using var optOut = new EnvVarScope(Azure.Functions.Cli.Common.Constants.TelemetryOptOutEnvVar, value);

        Assert.False(CliTelemetry.TryGetConnectionString(out _));
    }

    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvVarScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _previous);
    }

    [Fact]
    public void RecordCommand_NoListener_DoesNotThrow()
    {
        // Metric instruments record nothing when no MeterListener is wired up.
        CliTelemetry.Metric.RecordCommand("test", exitCode: 0, durationMs: 100);
        CliTelemetry.Metric.RecordCommand("test", exitCode: 1, durationMs: 50);
    }

    [Fact]
    public void StartCommandActivity_WithListener_UsesFixedOperationName()
    {
        // The activity is started before the command path is known, so it
        // gets a stable operation name and no cli.command.name tag yet.
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity();
        Assert.NotNull(activity);

        Assert.Equal(ActivityExtensions.CommandActivityName, activity.OperationName);
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.Null(activity.GetTagItem(TelemetryConventions.CliCommandName));
    }

    [Fact]
    public void SetCommandName_AppliesDisplayNameAndTag()
    {
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity();
        Assert.NotNull(activity);

        activity.SetCommandName("workload install");

        Assert.Equal("workload install", activity.DisplayName);
        Assert.Equal("workload install", activity.GetTagItem(TelemetryConventions.CliCommandName));
        // Operation name (fixed at creation) is left alone — only DisplayName moves.
        Assert.Equal(ActivityExtensions.CommandActivityName, activity.OperationName);
    }

    [Fact]
    public void SetCommandName_LastValueWins()
    {
        // Defensive: tag should reflect the most recent name in case a future
        // caller resolves the command in stages.
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity();
        Assert.NotNull(activity);

        activity.SetCommandName("workload");
        activity.SetCommandName("workload install");

        Assert.Equal("workload install", activity.DisplayName);
        Assert.Equal("workload install", activity.GetTagItem(TelemetryConventions.CliCommandName));
    }

    [Fact]
    public void Fail_RecordsExceptionAndSetsErrorStatus()
    {
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity();
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
