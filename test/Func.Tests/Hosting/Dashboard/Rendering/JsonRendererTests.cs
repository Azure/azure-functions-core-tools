// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Cli.Tests.Hosting.Dashboard.Rendering;

public class JsonRendererTests
{
    [Fact]
    public async Task EmitsSchemaVersionAndKindOnEveryRecord()
    {
        var (renderer, stream) = NewRenderer();
        var state = new DashboardState();
        await renderer.OnStartAsync(state, default);

        var entry = MakeEntry(
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "ready",
            });

        IReadOnlyList<DashboardEvent> events = state.Observe(entry);
        await renderer.OnEventAsync(entry, events, default);
        await renderer.OnSummaryAsync(state.BuildSummary("sigint", DateTimeOffset.UtcNow), default);
        await renderer.DisposeAsync();

        IReadOnlyList<JsonDocument> records = ReadAll(stream);
        Assert.NotEmpty(records);

        foreach (var doc in records)
        {
            Assert.Equal(1, doc.RootElement.GetProperty("schema_version").GetInt32());
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("kind").GetString()));
            Assert.False(string.IsNullOrEmpty(doc.RootElement.GetProperty("timestamp").GetString()));
        }
    }

    [Fact]
    public async Task FinalRecordIsSummary()
    {
        var (renderer, stream) = NewRenderer();
        var state = new DashboardState();

        await renderer.OnStartAsync(state, default);
        var entry = MakeEntry(
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.FunctionDiscovered,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionTriggerType] = "http",
            });
        await renderer.OnEventAsync(entry, state.Observe(entry), default);
        await renderer.OnSummaryAsync(state.BuildSummary("sigint", DateTimeOffset.UtcNow), default);
        await renderer.DisposeAsync();

        IReadOnlyList<JsonDocument> records = ReadAll(stream);
        Assert.Equal("summary", records[^1].RootElement.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task EmitsLogAndSyntheticRecords_InCausalOrder()
    {
        var (renderer, stream) = NewRenderer();
        var state = new DashboardState();
        await renderer.OnStartAsync(state, default);

        var entry = MakeEntry(
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.HostStateChanged,
                [HostLogAttributeKeys.HostState] = "ready",
            });

        await renderer.OnEventAsync(entry, state.Observe(entry), default);
        await renderer.OnSummaryAsync(state.BuildSummary("source_completed", DateTimeOffset.UtcNow), default);
        await renderer.DisposeAsync();

        IReadOnlyList<JsonDocument> records = ReadAll(stream);
        Assert.True(records.Count >= 3, $"Expected at least 3 records, got {records.Count}");
        Assert.Equal("log", records[0].RootElement.GetProperty("kind").GetString());
        Assert.Equal("host_state_changed", records[1].RootElement.GetProperty("kind").GetString());
    }

    private static (JsonRenderer renderer, MemoryStream stream) NewRenderer()
    {
        var stream = new MemoryStream();
        return (new JsonRenderer(stream, ownsStream: false), stream);
    }

    private static IReadOnlyList<JsonDocument> ReadAll(MemoryStream stream)
    {
        stream.Position = 0;
        var text = Encoding.UTF8.GetString(stream.ToArray());
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line))];
    }

    private static HostLogEntry MakeEntry(Dictionary<string, object?> attrs)
        => new(DateTimeOffset.UtcNow, "Host.Lifecycle", LogLevel.Information, default, "msg", null, attrs);
}
