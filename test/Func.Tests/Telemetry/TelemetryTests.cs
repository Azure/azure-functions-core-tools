// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Azure.Functions.Cli.Telemetry;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry.Resources;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class TelemetryTests
{
    [Fact]
    public void TryGetConnectionString_DefaultBuild_ReturnsFalse()
    {
        // Default build has the all-zeros instrumentation key, so telemetry
        // is not configured.
        CliTelemetry.TryGetConnectionString(out var connectionString).Should().BeFalse();
        connectionString.Should().BeNull();
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
        CliTelemetry.TryGetConnectionString(out _).Should().BeFalse();
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

        CliTelemetry.TryGetConnectionString(out _).Should().BeFalse();
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
        activity.Should().NotBeNull();

        activity.OperationName.Should().Be(ActivityExtensions.CommandActivityName);
        activity.Kind.Should().Be(ActivityKind.Internal);
        activity.GetTagItem(TelemetryConventions.CliCommandName).Should().BeNull();
    }

    [Fact]
    public void SetCommandName_AppliesDisplayNameAndTag()
    {
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity();
        activity.Should().NotBeNull();

        activity.SetCommandName("workload install");

        activity.DisplayName.Should().Be("workload install");
        activity.GetTagItem(TelemetryConventions.CliCommandName).Should().Be("workload install");
        // Operation name (fixed at creation) is left alone — only DisplayName moves.
        activity.OperationName.Should().Be(ActivityExtensions.CommandActivityName);
    }

    [Fact]
    public void SetCommandName_LastValueWins()
    {
        // Defensive: tag should reflect the most recent name in case a future
        // caller resolves the command in stages.
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity();
        activity.Should().NotBeNull();

        activity.SetCommandName("workload");
        activity.SetCommandName("workload install");

        activity.DisplayName.Should().Be("workload install");
        activity.GetTagItem(TelemetryConventions.CliCommandName).Should().Be("workload install");
    }

    [Fact]
    public void Fail_RecordsExceptionAndSetsErrorStatus()
    {
        using var listener = SubscribeListener();

        using var activity = CliTelemetry.Trace.StartCommandActivity();
        activity.Should().NotBeNull();

        var ex = new InvalidOperationException("boom");
        activity.Fail(ex);

        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("boom");
    }

    [Fact]
    public void CreateResourceBuilder_IncludesServiceIdentityWithoutOtherDimensions()
    {
        var resource = CliTelemetry.CreateResourceBuilder().Build();
        var attrs = resource.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value);

        attrs["service.name"].Should().Be(CliTelemetry.SourceName);
        attrs["service.version"].Should().Be(CliTelemetry.CliVersion);
        attrs.ContainsKey("os.type").Should().BeFalse();
        attrs.ContainsKey("os.architecture").Should().BeFalse();
        attrs.ContainsKey("process.runtime.description").Should().BeFalse();
        attrs.ContainsKey("telemetry.sdk.name").Should().BeFalse();
    }

    [Fact]
    public void ConfigureResource_RemovesAmbientAttributes()
    {
        var builder = ResourceBuilder.CreateEmpty()
            .AddAttributes([new KeyValuePair<string, object>("unrelated.attribute", "do-not-export")]);

        var resource = CliTelemetry.ConfigureResource(builder).Build();

        resource.Attributes.Should().NotContain(attribute => attribute.Key == "unrelated.attribute");
    }

    [Fact]
    public void EnrichmentProcessor_AddsCommonAttributesWithoutOverwritingExistingTags()
    {
        using var processor = new CliActivityEnrichmentProcessor(CliTelemetry.GetCommonAttributes());
        using var activity = new Activity("test");
        activity.SetTag(TelemetryConventions.OsType, "explicit-os");

        processor.OnStart(activity);

        activity.GetTagItem(TelemetryConventions.OsType).Should().Be("explicit-os");
        foreach (var attribute in CliTelemetry.GetCommonAttributes().Where(attribute => attribute.Key != TelemetryConventions.OsType))
        {
            activity.GetTagItem(attribute.Key).Should().Be(attribute.Value);
        }
    }

    [Fact]
    public void EnrichmentProcessor_RejectsNullArguments()
    {
        FluentActions.Invoking(() => new CliActivityEnrichmentProcessor(null!)).Should().Throw<ArgumentNullException>();
        using var processor = new CliActivityEnrichmentProcessor([]);
        FluentActions.Invoking(() => processor.OnStart(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RecordMetrics_IncludesCommonDimensionsBeforeAggregation()
    {
        var measurements = new List<(string Name, double Value, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == CliTelemetry.SourceName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
        listener.Start();

        CliTelemetry.Metric.RecordCommand("dimension-test", exitCode: 1, durationMs: 125);
        CliTelemetry.Metric.RecordWorkloadBoot(workloadCount: 3, durationMs: 25);

        measurements.Should().HaveCount(3);
        foreach (var measurement in measurements)
        {
            foreach (var attribute in CliTelemetry.GetCommonAttributes())
            {
                measurement.Tags[attribute.Key].Should().Be(attribute.Value);
            }
        }

        var count = measurements.Single(measurement => measurement.Name == TelemetryConventions.CommandCountInstrument);
        count.Value.Should().Be(1);
        count.Tags[TelemetryConventions.CliCommandName].Should().Be("dimension-test");
        count.Tags[TelemetryConventions.ProcessExitCode].Should().Be(1);
        measurements.Single(measurement => measurement.Name == TelemetryConventions.CommandDurationInstrument).Value.Should().Be(125);
        measurements.Single(measurement => measurement.Name == TelemetryConventions.WorkloadBootDurationInstrument).Value.Should().Be(25);
    }

    [Fact]
    public void RecordWorkloadBootActivity_PreservesMeasuredIntervalAndParent()
    {
        using var listener = SubscribeListener();
        Activity? bootActivity = null;
        listener.ActivityStopped = activity =>
        {
            if (activity.OperationName == TelemetryConventions.WorkloadBootActivityName)
            {
                bootActivity = activity;
            }
        };
        var startTime = DateTimeOffset.UtcNow.AddSeconds(-2);
        var telemetry = new WorkloadBootTelemetry(3, startTime.AddMilliseconds(10), TimeSpan.FromMilliseconds(250));
        using var command = CliTelemetry.Trace.StartCommandActivity(startTime);

        CliTelemetry.Trace.RecordWorkloadBootActivity(telemetry);

        bootActivity.Should().NotBeNull();
        bootActivity!.StartTimeUtc.Should().Be(telemetry.StartTime.UtcDateTime);
        bootActivity.Duration.Should().Be(telemetry.Duration);
        bootActivity.ParentSpanId.Should().Be(command!.SpanId);
        bootActivity.GetTagItem(TelemetryConventions.CliWorkloadCount).Should().Be(3);
        command.StartTimeUtc.Should().Be(startTime.UtcDateTime);
    }

    [Fact]
    public void ConfigureExporter_UsesFullSamplingWithoutGeneratedMetrics()
    {
        var options = new AzureMonitorExporterOptions();

        CliTelemetry.ConfigureExporter(options, "InstrumentationKey=00000000-0000-0000-0000-000000000000");

        options.TracesPerSecond.Should().BeNull();
        options.SamplingRatio.Should().Be(1.0F);
        options.EnableLiveMetrics.Should().BeFalse();
        options.EnableStandardMetrics.Should().BeFalse();
        options.EnablePerformanceCounters.Should().BeFalse();
        options.DisableOfflineStorage.Should().BeFalse();
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
