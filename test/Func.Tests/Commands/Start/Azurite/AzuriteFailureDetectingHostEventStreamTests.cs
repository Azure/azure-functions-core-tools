// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Azurite;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteFailureDetectingHostEventStreamTests
{
    private const string Guidance = "Delete the Azurite data directory to reset it.";

    [Fact]
    public async Task PassesEntriesThrough_AndInjectsGuidanceOnce_OnDetection()
    {
        var inner = new InMemoryHostEventStream();
        inner.Write(Entry("Status: 500 (Internal Server Error)"));
        inner.Write(Entry("Server: Azurite-Blob/3.35.0"));
        inner.Write(Entry("Server: Azurite-Blob/3.35.0"));
        inner.Complete();

        await using var stream = new AzuriteFailureDetectingHostEventStream(inner, Guidance);
        List<HostLogEntry> observed = await DrainAsync(stream);

        // Three host entries pass through plus exactly one injected guidance entry.
        observed.Should().HaveCount(4);
        HostLogEntry guidance = observed.Should().ContainSingle(e => e.Message == Guidance).Which;
        guidance.Level.Should().Be(LogLevel.Warning);
        guidance.Category.Should().Be("Azurite");
        stream.Detected.Should().BeTrue();
    }

    [Fact]
    public async Task BenignOutput_PassesThrough_WithoutInjection()
    {
        var inner = new InMemoryHostEventStream();
        inner.Write(Entry("Now listening on: http://0.0.0.0:7071"));
        inner.Write(Entry("Worker process started."));
        inner.Complete();

        await using var stream = new AzuriteFailureDetectingHostEventStream(inner, Guidance);
        List<HostLogEntry> observed = await DrainAsync(stream);

        observed.Should().HaveCount(2);
        observed.Should().NotContain(e => e.Message == Guidance);
        stream.Detected.Should().BeFalse();
    }

    private static async Task<List<HostLogEntry>> DrainAsync(IHostEventStream stream)
    {
        List<HostLogEntry> entries = [];
        await foreach (HostLogEntry entry in stream.ReadAsync(CancellationToken.None))
        {
            entries.Add(entry);
        }

        return entries;
    }

    private static HostLogEntry Entry(string message)
        => new(
            DateTimeOffset.UtcNow,
            "Host.Process",
            LogLevel.Information,
            default,
            message,
            Exception: null,
            HostLogEntry.EmptyAttributes);
}
