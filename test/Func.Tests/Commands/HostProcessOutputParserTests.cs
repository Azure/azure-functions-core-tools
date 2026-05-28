// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands;

public class HostProcessOutputParserTests
{
    [Fact]
    public void ParseLine_WhenStdout_MapsToInformationWithStreamAttribute()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(HostProcessStreamNames.StandardOutput, "Host started", DateTimeOffset.UnixEpoch);

        Assert.Equal("Host.Process", entry.Category);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Host started", entry.Message);
        Assert.Equal(HostProcessStreamNames.StandardOutput, entry.GetAttribute<string>(HostLogAttributeKeys.Stream));
    }

    [Fact]
    public void ParseLine_WhenStderr_MapsToErrorWithStreamAttribute()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(HostProcessStreamNames.StandardError, "Host failed", DateTimeOffset.UnixEpoch);

        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Host failed", entry.Message);
        Assert.Equal(HostProcessStreamNames.StandardError, entry.GetAttribute<string>(HostLogAttributeKeys.Stream));
    }

    [Fact]
    public void ParseLine_WhenStructuredHostRecord_MapsRecordFieldsAndAttributes()
    {
        var parser = new LineHostProcessOutputParser();
        string line = """
            {
              "source": "azure-functions-cli-host",
              "schema_version": 1,
              "timestamp": "2026-05-26T12:00:00.0000000+00:00",
              "category": "Function.HttpTrigger1.User",
              "level": "warning",
              "event_id": { "id": 42, "name": "UserLog" },
              "message": "hello from user code",
              "attributes": {
                "cli.event_kind": "log",
                "duration_ms": 12.5,
                "http.status_code": 202,
                "function.http_methods": [ "get", "post" ]
              },
              "exception": {
                "type": "System.InvalidOperationException",
                "message": "boom",
                "stack": "remote stack"
              }
            }
            """;

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            Minify(line),
            DateTimeOffset.UnixEpoch);

        Assert.Equal(DateTimeOffset.Parse("2026-05-26T12:00:00.0000000+00:00"), entry.Timestamp);
        Assert.Equal("Function.HttpTrigger1.User", entry.Category);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(42, entry.EventId.Id);
        Assert.Equal("UserLog", entry.EventId.Name);
        Assert.Equal("hello from user code", entry.Message);
        Assert.Equal("HttpTrigger1", entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName));
        Assert.Equal(HostProcessStreamNames.StandardOutput, entry.GetAttribute<string>(HostLogAttributeKeys.Stream));
        Assert.Equal(12.5, entry.GetAttribute<double>(HostLogAttributeKeys.DurationMs));
        Assert.Equal(202, entry.GetAttribute<int>(HostLogAttributeKeys.HttpStatusCode));
        string[]? methods = entry.GetAttribute<string[]>(HostLogAttributeKeys.FunctionHttpMethods);
        Assert.NotNull(methods);
        Assert.Equal(["get", "post"], methods);
        Assert.Equal("boom", entry.Exception?.Message);
    }

    [Fact]
    public void ParseLine_WhenStructuredInvocationMessage_EnrichesInvocationAttributes()
    {
        var parser = new LineHostProcessOutputParser();
        string line = """
            {
              "source": "azure-functions-cli-host",
              "schema_version": 1,
              "category": "Host.Results",
              "level": "information",
              "message": "Executed 'Functions.HttpTrigger1' (Succeeded, Id=11111111-1111-1111-1111-111111111111, Duration=123.45ms)",
              "attributes": { }
            }
            """;

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            Minify(line),
            DateTimeOffset.UnixEpoch);

        Assert.Equal("HttpTrigger1", entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName));
        Assert.Equal("11111111-1111-1111-1111-111111111111", entry.GetAttribute<string>(HostLogAttributeKeys.FunctionInvocationId));
        Assert.Equal(CliEventKinds.InvocationCompleted, entry.GetAttribute<string>(HostLogAttributeKeys.CliEventKind));
        Assert.Equal("succeeded", entry.GetAttribute<string>(HostLogAttributeKeys.FunctionResult));
        Assert.Equal(123.45, entry.GetAttribute<double>(HostLogAttributeKeys.DurationMs));
    }

    [Fact]
    public void ParseLine_WhenConsoleLoggerHeader_ParsesLevelCategoryAndEventId()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            "warn: Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService[12]",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService", entry.Category);
        Assert.Equal(12, entry.EventId.Id);
        Assert.Equal(string.Empty, entry.Message);
        Assert.Equal(HostProcessStreamNames.StandardOutput, entry.GetAttribute<string>(HostLogAttributeKeys.Stream));
    }

    [Fact]
    public void ParseLine_WhenConsoleLoggerContinuation_UsesPreviousHeaderContext()
    {
        var parser = new LineHostProcessOutputParser();
        parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            "fail: Function.HttpTrigger1.User[3]",
            DateTimeOffset.UnixEpoch);

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            "      Executed 'Functions.HttpTrigger1' (Failed, Id=abc123, Duration=7ms)",
            DateTimeOffset.UnixEpoch);

        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("Function.HttpTrigger1.User", entry.Category);
        Assert.Equal(3, entry.EventId.Id);
        Assert.Equal("Executed 'Functions.HttpTrigger1' (Failed, Id=abc123, Duration=7ms)", entry.Message);
        Assert.Equal("HttpTrigger1", entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName));
        Assert.Equal("abc123", entry.GetAttribute<string>(HostLogAttributeKeys.FunctionInvocationId));
        Assert.Equal(CliEventKinds.InvocationCompleted, entry.GetAttribute<string>(HostLogAttributeKeys.CliEventKind));
        Assert.Equal("failed", entry.GetAttribute<string>(HostLogAttributeKeys.FunctionResult));
        Assert.Equal(7, entry.GetAttribute<double>(HostLogAttributeKeys.DurationMs));
    }

    [Theory]
    [InlineData("""{"source":"something-else","schema_version":1,"message":"ignored"}""")]
    [InlineData("""{"source":"azure-functions-cli-host","schema_version":2,"message":"ignored"}""")]
    [InlineData("""{"source":"azure-functions-cli-host","schema_version":1,"message":""")]
    public void ParseLine_WhenJsonIsNotStructuredHostRecord_FallsBackToLineRecord(string line)
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(HostProcessStreamNames.StandardOutput, line, DateTimeOffset.UnixEpoch);

        Assert.Equal("Host.Process", entry.Category);
        Assert.Equal(line, entry.Message);
        Assert.Equal(HostProcessStreamNames.StandardOutput, entry.GetAttribute<string>(HostLogAttributeKeys.Stream));
    }

    private static string Minify(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
