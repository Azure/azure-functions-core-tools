// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class HostStructuredEventWriterTests
{
    [Fact]
    public void WriteLog_WritesStructuredHostEnvelope()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cli.event_kind"] = "host_state_changed",
            ["host.state"] = "ready",
            ["duration_ms"] = 12.5,
        };
        var state = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["PreviousState"] = "Initialized",
            ["NewState"] = "Running",
        };
        var scopes = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["values"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["TraceId"] = "trace-1",
                },
            },
        };

        HostStructuredEventWriter.WriteLog(
            "Host.General",
            LogLevel.Information,
            new EventId(529, "HostStateChanged"),
            "Host state changed from Initialized to Running",
            attributes,
            state: state,
            scopes: scopes,
            writer: writer);

        using var document = JsonDocument.Parse(writer.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal(HostStructuredEventWriter.Source, root.GetProperty("source").GetString());
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("log", root.GetProperty("record_type").GetString());
        Assert.Equal("Host.General", root.GetProperty("category").GetString());
        Assert.Equal("information", root.GetProperty("level").GetString());
        Assert.Equal(529, root.GetProperty("event_id").GetProperty("id").GetInt32());
        Assert.Equal("HostStateChanged", root.GetProperty("event_id").GetProperty("name").GetString());
        Assert.Equal("Host state changed from Initialized to Running", root.GetProperty("message").GetString());
        Assert.Equal("ready", root.GetProperty("attributes").GetProperty("host.state").GetString());
        Assert.Equal(12.5, root.GetProperty("attributes").GetProperty("duration_ms").GetDouble());
        Assert.Equal("Running", root.GetProperty("state").GetProperty("NewState").GetString());
        Assert.Equal("trace-1", root.GetProperty("scopes")[0].GetProperty("values").GetProperty("TraceId").GetString());
    }

    [Theory]
    [InlineData("Microsoft.Azure.WebJobs.Script.DependencyInjection.ScriptStartupTypeLocator", "ScriptStartupTypeLocator")]
    [InlineData("Microsoft.Azure.WebJobs.Script.WebHost.Middleware.SystemTraceMiddleware", "SystemTraceMiddleware")]
    [InlineData("Microsoft.Azure.WebJobs.TypeName", "TypeName")]
    [InlineData("Host.Triggers.Timer.Listener.TimerListener", "TimerListener")]
    [InlineData("Host.Triggers.TypeName", "TypeName")]
    [InlineData("Host.General", "Host.General")]
    [InlineData("Function.HttpTrigger1.User", "Function.HttpTrigger1.User")]
    public void NormalizeCategory_AppliesKnownPrefixRules(string category, string expected)
    {
        Assert.Equal(expected, HostStructuredEventWriter.NormalizeCategory(category));
    }

    [Fact]
    public void WriteLog_NormalizesKnownCategoryPrefixes()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);

        HostStructuredEventWriter.WriteLog(
            "Microsoft.Azure.WebJobs.Script.DependencyInjection.ScriptStartupTypeLocator",
            LogLevel.Information,
            new EventId(0),
            "Startup type located.",
            new Dictionary<string, object?>(StringComparer.Ordinal),
            writer: writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("ScriptStartupTypeLocator", document.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public void WriteLog_WithNestedException_WritesInnerExceptionDetails()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var exception = new InvalidOperationException("outer failure", new Exception("inner failure"));

        HostStructuredEventWriter.WriteLog(
            "Function.HttpTrigger1.User",
            LogLevel.Error,
            new EventId(0),
            "Invocation failed",
            new Dictionary<string, object?>(StringComparer.Ordinal),
            exception,
            writer: writer);

        using var document = JsonDocument.Parse(writer.ToString());
        JsonElement exceptionJson = document.RootElement.GetProperty("exception");
        Assert.Equal(typeof(InvalidOperationException).FullName, exceptionJson.GetProperty("type").GetString());
        Assert.Equal("outer failure", exceptionJson.GetProperty("message").GetString());

        JsonElement innerExceptionJson = exceptionJson.GetProperty("inner_exception");
        Assert.Equal(typeof(Exception).FullName, innerExceptionJson.GetProperty("type").GetString());
        Assert.Equal("inner failure", innerExceptionJson.GetProperty("message").GetString());
    }

    [Fact]
    public void WriteFunctionDiscovered_EmitsDashboardMetadata()
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var metadata = new FunctionMetadata
        {
            Name = "HttpTrigger1",
            FunctionDirectory = @"C:\functions\HttpTrigger1",
            Language = "dotnet-isolated",
            ScriptFile = "run.csx",
            EntryPoint = "Functions.HttpTrigger1.Run",
        };
        var trigger = new BindingMetadata
        {
            Name = "req",
            Type = "httpTrigger",
            Direction = BindingDirection.In,
        };
        trigger.Properties["route"] = "widgets/{id}";
        trigger.Properties["methods"] = new[] { "get", "post" };
        trigger.Connection = "UseDevelopmentStorage=true";
        trigger.Properties["customSecret"] = "user-supplied-value";
        metadata.Bindings.Add(trigger);

        HostStructuredEventWriter.WriteFunctionDiscovered(metadata, writer);

        using var document = JsonDocument.Parse(writer.ToString());
        JsonElement attributes = document.RootElement.GetProperty("attributes");
        Assert.Equal("function_discovered", attributes.GetProperty("cli.event_kind").GetString());
        Assert.Equal("HttpTrigger1", attributes.GetProperty("function.name").GetString());
        Assert.Equal(@"C:\functions\HttpTrigger1", attributes.GetProperty("function.id").GetString());
        Assert.Equal("http", attributes.GetProperty("function.trigger_type").GetString());
        Assert.Equal("/api/widgets/{id}", attributes.GetProperty("function.route").GetString());
        Assert.Equal("dotnet-isolated", attributes.GetProperty("function.language").GetString());
        Assert.Equal("run.csx", attributes.GetProperty("function.script_file").GetString());
        Assert.Equal("Functions.HttpTrigger1.Run", attributes.GetProperty("function.entry_point").GetString());

        JsonElement methods = attributes.GetProperty("function.http_methods");
        Assert.Equal("GET", methods[0].GetString());
        Assert.Equal("POST", methods[1].GetString());

        JsonElement binding = attributes.GetProperty("function.bindings")[0];
        Assert.Equal("req", binding.GetProperty("name").GetString());
        Assert.Equal("httpTrigger", binding.GetProperty("type").GetString());
        Assert.Equal("In", binding.GetProperty("direction").GetString());
        Assert.True(binding.GetProperty("is_trigger").GetBoolean());
        Assert.False(binding.TryGetProperty("connection", out _));
        Assert.False(binding.TryGetProperty("route", out _));
        Assert.False(binding.TryGetProperty("methods", out _));
        Assert.False(binding.TryGetProperty("customSecret", out _));
    }

    [Fact]
    public void HostStructuredLoggerProvider_EnrichesFunctionAndInvocationAttributes()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using var stderr = new StringWriter(CultureInfo.InvariantCulture);
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new HostStructuredLoggerProvider(stdout, stderr));
        });

        ILogger logger = loggerFactory.CreateLogger("Function.HttpTrigger1.User");
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = "trace-1",
            ["SpanId"] = "span-1",
            ["ParentId"] = "parent-1",
        });

        logger.LogInformation("Executed 'Functions.HttpTrigger1' (Failed, Id=abc123, Duration=7ms)");

        using var document = JsonDocument.Parse(stdout.ToString());
        JsonElement attributes = document.RootElement.GetProperty("attributes");
        Assert.Equal("HttpTrigger1", attributes.GetProperty("function.name").GetString());
        Assert.Equal("abc123", attributes.GetProperty("function.invocation_id").GetString());
        Assert.Equal("invocation_completed", attributes.GetProperty("cli.event_kind").GetString());
        Assert.Equal("failed", attributes.GetProperty("function.result").GetString());
        Assert.Equal(7, attributes.GetProperty("duration_ms").GetDouble());
        Assert.Equal("trace-1", attributes.GetProperty("trace_id").GetString());
        Assert.Equal("span-1", attributes.GetProperty("span_id").GetString());
        Assert.Equal("parent-1", attributes.GetProperty("parent_span_id").GetString());
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void HostStructuredLoggerProvider_EnrichesHostStateAttributes()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new HostStructuredLoggerProvider(stdout, TextWriter.Null));
        });

        ILogger logger = loggerFactory.CreateLogger("Host.General");
        logger.LogDebug(
            new EventId(529, "HostStateChanged"),
            "Host state changed from {previousState} to {newState}.",
            "Initialized",
            "Running");

        using var document = JsonDocument.Parse(stdout.ToString());
        JsonElement attributes = document.RootElement.GetProperty("attributes");
        Assert.Equal("host_state_changed", attributes.GetProperty("cli.event_kind").GetString());
        Assert.Equal("ready", attributes.GetProperty("host.state").GetString());
    }

    [Fact]
    public void HostStructuredLoggerProvider_EnrichesHttpRequestAttributes()
    {
        using var stdout = new StringWriter(CultureInfo.InvariantCulture);
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new HostStructuredLoggerProvider(stdout, TextWriter.Null));
        });

        ILogger logger = loggerFactory.CreateLogger("Microsoft.Azure.WebJobs.Script.WebHost.Middleware.SystemTraceMiddleware");
        logger.LogInformation(
            new EventId(528, "ExecutedHttpRequest"),
            "Executed HTTP request {httpMethod} {uri} with status {statusCode} in {duration}ms.",
            "GET",
            "http://localhost/api/HttpTrigger1",
            200,
            42.25);

        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("SystemTraceMiddleware", document.RootElement.GetProperty("category").GetString());
        JsonElement attributes = document.RootElement.GetProperty("attributes");
        Assert.Equal("GET", attributes.GetProperty("http.method").GetString());
        Assert.Equal("http://localhost/api/HttpTrigger1", attributes.GetProperty("http.target").GetString());
        Assert.Equal(200, attributes.GetProperty("http.status_code").GetInt32());
        Assert.Equal(42.25, attributes.GetProperty("duration_ms").GetDouble());
    }
}
