// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Hosting.Events;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Azure.Functions.Cli.Tests.Commands;

public class HostProcessOutputParserTests
{
    [Fact]
    public void ParseLine_WhenStdout_MapsToInformationWithStreamAttribute()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(HostProcessStreamNames.StandardOutput, "Host started", DateTimeOffset.UnixEpoch);

        entry.Category.Should().Be("Host.Process");
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().Be("Host started");
        entry.GetAttribute<string>(HostLogAttributeKeys.Stream).Should().Be(HostProcessStreamNames.StandardOutput);
    }

    [Fact]
    public void ParseLine_WhenStderr_MapsToErrorWithStreamAttribute()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(HostProcessStreamNames.StandardError, "Host failed", DateTimeOffset.UnixEpoch);

        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Be("Host failed");
        entry.GetAttribute<string>(HostLogAttributeKeys.Stream).Should().Be(HostProcessStreamNames.StandardError);
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
                "stack": "remote stack",
                "inner_exception": {
                  "type": "Worker.UserException",
                  "message": "inner boom",
                  "stack": "inner stack"
                }
              }
            }
            """;

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            Minify(line),
            DateTimeOffset.UnixEpoch);

        entry.Timestamp.Should().Be(DateTimeOffset.Parse("2026-05-26T12:00:00.0000000+00:00"));
        entry.Category.Should().Be("Function.HttpTrigger1.User");
        entry.Level.Should().Be(LogLevel.Warning);
        entry.EventId.Id.Should().Be(42);
        entry.EventId.Name.Should().Be("UserLog");
        entry.Message.Should().Be("hello from user code");
        entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName).Should().Be("HttpTrigger1");
        entry.GetAttribute<string>(HostLogAttributeKeys.Stream).Should().Be(HostProcessStreamNames.StandardOutput);
        entry.GetAttribute<double>(HostLogAttributeKeys.DurationMs).Should().Be(12.5);
        entry.GetAttribute<int>(HostLogAttributeKeys.HttpStatusCode).Should().Be(202);
        string[]? methods = entry.GetAttribute<string[]>(HostLogAttributeKeys.FunctionHttpMethods);
        methods.Should().NotBeNull();
        methods.Should().Equal(["get", "post"]);
        (entry.Exception?.Message).Should().Be("boom");
        entry.ExceptionDetails.Should().NotBeNull();
        entry.ExceptionDetails.Type.Should().Be("System.InvalidOperationException");
        entry.ExceptionDetails.Stack.Should().Be("remote stack");
        entry.ExceptionDetails.InnerException.Should().NotBeNull();
        entry.ExceptionDetails.InnerException.Type.Should().Be("Worker.UserException");
        entry.ExceptionDetails.InnerException.Message.Should().Be("inner boom");
        entry.ExceptionDetails.InnerException.Stack.Should().Be("inner stack");
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

        entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName).Should().Be("HttpTrigger1");
        entry.GetAttribute<string>(HostLogAttributeKeys.FunctionInvocationId).Should().Be("11111111-1111-1111-1111-111111111111");
        entry.GetAttribute<string>(HostLogAttributeKeys.CliEventKind).Should().Be(CliEventKinds.InvocationCompleted);
        entry.GetAttribute<string>(HostLogAttributeKeys.FunctionResult).Should().Be("succeeded");
        entry.GetAttribute<double>(HostLogAttributeKeys.DurationMs).Should().Be(123.45);
    }

    [Fact]
    public void ParseLine_WhenConsoleLoggerHeader_ParsesLevelCategoryAndEventId()
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(
            HostProcessStreamNames.StandardOutput,
            "warn: Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService[12]",
            DateTimeOffset.UnixEpoch);

        entry.Level.Should().Be(LogLevel.Warning);
        entry.Category.Should().Be("Microsoft.Azure.WebJobs.Hosting.OptionsLoggingService");
        entry.EventId.Id.Should().Be(12);
        entry.Message.Should().Be(string.Empty);
        entry.GetAttribute<string>(HostLogAttributeKeys.Stream).Should().Be(HostProcessStreamNames.StandardOutput);
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

        entry.Level.Should().Be(LogLevel.Error);
        entry.Category.Should().Be("Function.HttpTrigger1.User");
        entry.EventId.Id.Should().Be(3);
        entry.Message.Should().Be("Executed 'Functions.HttpTrigger1' (Failed, Id=abc123, Duration=7ms)");
        entry.GetAttribute<string>(HostLogAttributeKeys.FunctionName).Should().Be("HttpTrigger1");
        entry.GetAttribute<string>(HostLogAttributeKeys.FunctionInvocationId).Should().Be("abc123");
        entry.GetAttribute<string>(HostLogAttributeKeys.CliEventKind).Should().Be(CliEventKinds.InvocationCompleted);
        entry.GetAttribute<string>(HostLogAttributeKeys.FunctionResult).Should().Be("failed");
        entry.GetAttribute<double>(HostLogAttributeKeys.DurationMs).Should().Be(7);
    }

    [Theory]
    [InlineData("""{"source":"something-else","schema_version":1,"message":"ignored"}""")]
    [InlineData("""{"source":"azure-functions-cli-host","schema_version":2,"message":"ignored"}""")]
    [InlineData("""{"source":"azure-functions-cli-host","schema_version":1,"message":""")]
    public void ParseLine_WhenJsonIsNotStructuredHostRecord_FallsBackToLineRecord(string line)
    {
        var parser = new LineHostProcessOutputParser();

        HostLogEntry entry = parser.ParseLine(HostProcessStreamNames.StandardOutput, line, DateTimeOffset.UnixEpoch);

        entry.Category.Should().Be("Host.Process");
        entry.Message.Should().Be(line);
        entry.GetAttribute<string>(HostLogAttributeKeys.Stream).Should().Be(HostProcessStreamNames.StandardOutput);
    }

    private static string Minify(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement);
    }
}
