// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Initialization;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class StartDashboardEventStreamFactoryTests
{
    [Fact]
    public async Task Create_WhenCompact_PrependsCompletedInitializationSteps()
    {
        var factory = new StartDashboardEventStreamFactory();
        var hostStream = new InMemoryHostEventStream();
        hostStream.Write(new HostLogEntry(
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            "Host.Startup",
            LogLevel.Information,
            default,
            "Host ready",
            Exception: null,
            HostLogEntry.EmptyAttributes));
        hostStream.Complete();
        StartInitializationEvent[] initializationEvents =
        [
            new StartInitializationStepStartedEvent(
                DateTimeOffset.UnixEpoch,
                new StartInitializationStep(ResolveProfileInitializationStep.StepId, "Resolve profile")),
            new StartInitializationStepCompletedEvent(
                DateTimeOffset.UnixEpoch,
                ResolveProfileInitializationStep.StepId,
                "None (no profile applied)"),
        ];

        IHostEventStream stream = factory.Create(OutputMode.Compact, initializationEvents, hostStream);
        HostLogEntry[] entries = await ReadAllAsync(stream);

        Assert.Equal(2, entries.Length);
        Assert.Equal("[startup]", entries[0].Category);
        Assert.Equal("Resolve profile: None (no profile applied)", entries[0].Message);
        Assert.Equal(LogLevel.Information, entries[0].Level);
        Assert.Equal("start_initialization_step_completed", entries[0].GetAttribute<string>(HostLogAttributeKeys.CliEventKind));
        Assert.Equal("Host.Startup", entries[1].Category);
        Assert.Equal("Host ready", entries[1].Message);
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("json")]
    public void Create_WhenModeAlreadyPersistsInitializationOutput_ReturnsHostStream(string outputModeName)
    {
        var factory = new StartDashboardEventStreamFactory();
        var hostStream = new InMemoryHostEventStream();
        OutputMode outputMode = outputModeName == "json" ? OutputMode.Json : OutputMode.Plain;
        StartInitializationEvent[] initializationEvents =
        [
            new StartInitializationStepCompletedEvent(
                DateTimeOffset.UnixEpoch,
                ResolveProfileInitializationStep.StepId,
                "None (no profile applied)"),
        ];

        IHostEventStream stream = factory.Create(outputMode, initializationEvents, hostStream);

        Assert.Same(hostStream, stream);
    }

    private static async Task<HostLogEntry[]> ReadAllAsync(IHostEventStream stream)
    {
        List<HostLogEntry> entries = [];
        await foreach (HostLogEntry entry in stream.ReadAsync(CancellationToken.None))
        {
            entries.Add(entry);
        }

        return [.. entries];
    }
}
