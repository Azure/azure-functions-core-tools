// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Telemetry;
using Xunit;

namespace Azure.Functions.Cli.Tests.Telemetry;

public class TelemetryTests
{
    [Fact]
    public void NoOpTelemetryClient_IsNotEnabled()
    {
        var client = new NoOpTelemetryClient();
        Assert.False(client.IsEnabled);
    }

    [Fact]
    public void NoOpTelemetryClient_TrackCommand_DoesNotThrow()
    {
        var client = new NoOpTelemetryClient();
        client.TrackCommand("test", true, 100);
        client.TrackCommand("test", false, 50, new Dictionary<string, string> { ["key"] = "value" });
    }

    [Fact]
    public void NoOpTelemetryClient_TrackException_DoesNotThrow()
    {
        var client = new NoOpTelemetryClient();
        client.TrackException(new InvalidOperationException("test"));
    }

    [Fact]
    public void NoOpTelemetryClient_Flush_DoesNotThrow()
    {
        var client = new NoOpTelemetryClient();
        client.Flush();
    }

    [Fact]
    public void AppInsightsTelemetryClient_DisabledWithZeroKey()
    {
        // Default key is all zeros (no CI override), so should be disabled
        var client = new AppInsightsTelemetryClient();
        Assert.False(client.IsEnabled);
        client.Dispose();
    }

    [Fact]
    public void AppInsightsTelemetryClient_DisabledDoesNotThrow()
    {
        // With telemetry disabled, all operations should be no-ops
        var client = new AppInsightsTelemetryClient();
        Assert.False(client.IsEnabled);

        client.TrackCommand("test", true, 100);
        client.TrackException(new Exception("test"));
        Assert.Null(client.StartCommandActivity("test"));
        client.Flush();
        client.Dispose();
    }
}
