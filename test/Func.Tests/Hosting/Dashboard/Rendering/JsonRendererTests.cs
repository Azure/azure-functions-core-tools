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

    [Fact]
    public async Task EmitsRemoteExceptionDetailsForLogsAndInvocationEvents()
    {
        var (renderer, stream) = NewRenderer();
        var state = new DashboardState();
        await renderer.OnStartAsync(state, default);
        var exceptionDetails = new HostLogExceptionDetails(
            "Microsoft.Azure.WebJobs.Host.FunctionInvocationException",
            "Exception while executing function",
            "outer stack",
            new HostLogExceptionDetails("Worker.UserException", "inner boom", "inner stack", null));
        var entry = new HostLogEntry(
            DateTimeOffset.UtcNow,
            "Function.HttpTrigger1.User",
            LogLevel.Error,
            default,
            "Executed 'Functions.HttpTrigger1' (Failed, Id=id-1, Duration=10ms)",
            new InvalidOperationException("placeholder"),
            new Dictionary<string, object?>
            {
                [HostLogAttributeKeys.CliEventKind] = CliEventKinds.InvocationCompleted,
                [HostLogAttributeKeys.FunctionName] = "HttpTrigger1",
                [HostLogAttributeKeys.FunctionInvocationId] = "id-1",
                [HostLogAttributeKeys.FunctionResult] = "failed",
                [HostLogAttributeKeys.DurationMs] = 10.0,
            })
        {
            ExceptionDetails = exceptionDetails,
        };

        await renderer.OnEventAsync(entry, state.Observe(entry), default);
        await renderer.DisposeAsync();

        IReadOnlyList<JsonDocument> records = ReadAll(stream);
        JsonElement logException = records
            .Single(doc => doc.RootElement.GetProperty("kind").GetString() == "log")
            .RootElement
            .GetProperty("exception");
        Assert.Equal("Microsoft.Azure.WebJobs.Host.FunctionInvocationException", logException.GetProperty("type").GetString());
        Assert.Equal("outer stack", logException.GetProperty("stack").GetString());
        Assert.Equal("Worker.UserException", logException.GetProperty("inner_exception").GetProperty("type").GetString());
        Assert.Equal("inner stack", logException.GetProperty("inner_exception").GetProperty("stack").GetString());

        JsonElement eventError = records
            .Single(doc => doc.RootElement.GetProperty("kind").GetString() == "invocation_completed")
            .RootElement
            .GetProperty("error");
        Assert.Equal("Microsoft.Azure.WebJobs.Host.FunctionInvocationException", eventError.GetProperty("type").GetString());
        Assert.Equal("Exception while executing function", eventError.GetProperty("message").GetString());
        Assert.Equal("outer stack", eventError.GetProperty("stack").GetString());
        Assert.Equal("Worker.UserException", eventError.GetProperty("inner_exception").GetProperty("type").GetString());
        Assert.Equal("inner boom", eventError.GetProperty("inner_exception").GetProperty("message").GetString());
        Assert.Equal("inner stack", eventError.GetProperty("inner_exception").GetProperty("stack").GetString());
    }

    [Fact]
    public async Task EmitsExceptionOnlyLogRecords()
    {
        var (renderer, stream) = NewRenderer();
        var exceptionDetails = new HostLogExceptionDetails(
            "Microsoft.Azure.WebJobs.Script.Workers.WorkerProcessExitException",
            "A connection string was not found. Please set your connection string.",
            null,
            null);
        var entry = new HostLogEntry(
            DateTimeOffset.UtcNow,
            "Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService",
            LogLevel.Error,
            default,
            string.Empty,
            null,
            new Dictionary<string, object?>())
        {
            ExceptionDetails = exceptionDetails,
        };

        await renderer.OnEventAsync(entry, [], default);
        await renderer.DisposeAsync();

        JsonElement exception = Assert.Single(ReadAll(stream))
            .RootElement
            .GetProperty("exception");
        Assert.Equal("Microsoft.Azure.WebJobs.Script.Workers.WorkerProcessExitException", exception.GetProperty("type").GetString());
        Assert.Equal("A connection string was not found. Please set your connection string.", exception.GetProperty("message").GetString());
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
