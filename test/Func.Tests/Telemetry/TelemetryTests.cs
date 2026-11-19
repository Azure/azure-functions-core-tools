// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Telemetry;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class TelemetryTests
{
    private const string InstrumentationKey = "11111111-1111-1111-1111-111111111111";
    private const string ExpectedConnectionString = "InstrumentationKey=11111111-1111-1111-1111-111111111111";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryGetConnectionString_UnconfiguredKey_ReturnsFalse(string? key)
    {
        bool result = CliTelemetry.TryGetConnectionString(key, null, null, out var connectionString);

        result.Should().BeFalse();
        connectionString.Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void TryGetConnectionString_OptOutUnsetOrEmpty_ReturnsConnectionString(string? optOutValue, string? legacyOptOutValue)
    {
        bool result = CliTelemetry.TryGetConnectionString(InstrumentationKey, optOutValue, legacyOptOutValue, out var connectionString);

        result.Should().BeTrue();
        connectionString.Should().Be(ExpectedConnectionString);
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData(null, "0")]
    [InlineData("false", null)]
    [InlineData(null, "false")]
    [InlineData("no", null)]
    [InlineData(null, "no")]
    [InlineData("n", null)]
    [InlineData(null, "n")]
    [InlineData("off", null)]
    [InlineData(null, "off")]
    [InlineData("FALSE", null)]
    [InlineData(null, "FALSE")]
    [InlineData("OfF", null)]
    [InlineData(null, "OfF")]
    [InlineData("  off  ", null)]
    [InlineData(null, "  off  ")]
    [InlineData("false", "off")]
    public void TryGetConnectionString_OptOutFalseSentinels_ReturnsConnectionString(string? optOutValue, string? legacyOptOutValue)
    {
        bool result = CliTelemetry.TryGetConnectionString(InstrumentationKey, optOutValue, legacyOptOutValue, out var connectionString);

        result.Should().BeTrue();
        connectionString.Should().Be(ExpectedConnectionString);
    }

    [Fact]
    public void TryGetConnectionString_TabAndNewlinePaddedFalseSentinels_ReturnsConnectionString()
    {
        bool result = CliTelemetry.TryGetConnectionString(InstrumentationKey, "\tFALSE\n", "\r\nOff\t", out var connectionString);

        result.Should().BeTrue();
        connectionString.Should().Be(ExpectedConnectionString);
    }

    [Theory]
    [InlineData("1", null)]
    [InlineData(null, "1")]
    [InlineData("true", null)]
    [InlineData(null, "true")]
    [InlineData("yes", null)]
    [InlineData(null, "yes")]
    [InlineData("on", null)]
    [InlineData(null, "on")]
    [InlineData("anything-else", null)]
    [InlineData(null, "anything-else")]
    [InlineData(" ", null)]
    [InlineData(null, " ")]
    public void TryGetConnectionString_OptOutTruthyValues_ReturnsFalse(string? optOutValue, string? legacyOptOutValue)
    {
        bool result = CliTelemetry.TryGetConnectionString(InstrumentationKey, optOutValue, legacyOptOutValue, out var connectionString);

        result.Should().BeFalse();
        connectionString.Should().BeNull();
    }

    [Theory]
    [InlineData("true", "off")]
    [InlineData("off", "true")]
    [InlineData("true", "true")]
    public void TryGetConnectionString_EitherVariableOptsOut_ReturnsFalse(string optOutValue, string legacyOptOutValue)
    {
        bool result = CliTelemetry.TryGetConnectionString(InstrumentationKey, optOutValue, legacyOptOutValue, out var connectionString);

        result.Should().BeFalse();
        connectionString.Should().BeNull();
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
    public void CreateResourceBuilder_IncludesServiceAndOsAttributes()
    {
        var resource = CliTelemetry.CreateResourceBuilder().Build();
        var attrs = resource.Attributes.ToDictionary(kv => kv.Key, kv => kv.Value);

        attrs["service.name"].Should().Be(CliTelemetry.SourceName);
        attrs["service.version"].Should().Be(CliTelemetry.CliVersion);
        attrs.ContainsKey("os.type").Should().BeTrue();
        attrs.ContainsKey("os.architecture").Should().BeTrue();
        attrs.ContainsKey("process.runtime.description").Should().BeTrue();
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
