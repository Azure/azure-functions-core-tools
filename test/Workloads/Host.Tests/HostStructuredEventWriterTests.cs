// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Extensions.Logging;

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
        root.GetProperty("source").GetString().Should().Be(HostStructuredEventWriter.Source);
        root.GetProperty("schema_version").GetInt32().Should().Be(1);
        root.GetProperty("record_type").GetString().Should().Be("log");
        root.GetProperty("category").GetString().Should().Be("Host.General");
        root.GetProperty("level").GetString().Should().Be("information");
        root.GetProperty("event_id").GetProperty("id").GetInt32().Should().Be(529);
        root.GetProperty("event_id").GetProperty("name").GetString().Should().Be("HostStateChanged");
        root.GetProperty("message").GetString().Should().Be("Host state changed from Initialized to Running");
        root.GetProperty("attributes").GetProperty("host.state").GetString().Should().Be("ready");
        root.GetProperty("attributes").GetProperty("duration_ms").GetDouble().Should().Be(12.5);
        root.GetProperty("state").GetProperty("NewState").GetString().Should().Be("Running");
        root.GetProperty("scopes")[0].GetProperty("values").GetProperty("TraceId").GetString().Should().Be("trace-1");
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
        HostStructuredEventWriter.NormalizeCategory(category).Should().Be(expected);
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
        document.RootElement.GetProperty("category").GetString().Should().Be("ScriptStartupTypeLocator");
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
        exceptionJson.GetProperty("type").GetString().Should().Be(typeof(InvalidOperationException).FullName);
        exceptionJson.GetProperty("message").GetString().Should().Be("outer failure");

        JsonElement innerExceptionJson = exceptionJson.GetProperty("inner_exception");
        innerExceptionJson.GetProperty("type").GetString().Should().Be(typeof(Exception).FullName);
        innerExceptionJson.GetProperty("message").GetString().Should().Be("inner failure");
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
        attributes.GetProperty("cli.event_kind").GetString().Should().Be("function_discovered");
        attributes.GetProperty("function.name").GetString().Should().Be("HttpTrigger1");
        attributes.GetProperty("function.id").GetString().Should().Be(@"C:\functions\HttpTrigger1");
        attributes.GetProperty("function.trigger_type").GetString().Should().Be("http");
        attributes.GetProperty("function.route").GetString().Should().Be("/api/widgets/{id}");
        attributes.GetProperty("function.language").GetString().Should().Be("dotnet-isolated");
        attributes.GetProperty("function.script_file").GetString().Should().Be("run.csx");
        attributes.GetProperty("function.entry_point").GetString().Should().Be("Functions.HttpTrigger1.Run");

        JsonElement methods = attributes.GetProperty("function.http_methods");
        methods[0].GetString().Should().Be("GET");
        methods[1].GetString().Should().Be("POST");

        JsonElement binding = attributes.GetProperty("function.bindings")[0];
        binding.GetProperty("name").GetString().Should().Be("req");
        binding.GetProperty("type").GetString().Should().Be("httpTrigger");
        binding.GetProperty("direction").GetString().Should().Be("In");
        binding.GetProperty("is_trigger").GetBoolean().Should().BeTrue();
        binding.TryGetProperty("connection", out _).Should().BeFalse();
        binding.TryGetProperty("route", out _).Should().BeFalse();
        binding.TryGetProperty("methods", out _).Should().BeFalse();
        binding.TryGetProperty("customSecret", out _).Should().BeFalse();
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
        attributes.GetProperty("function.name").GetString().Should().Be("HttpTrigger1");
        attributes.GetProperty("function.invocation_id").GetString().Should().Be("abc123");
        attributes.GetProperty("cli.event_kind").GetString().Should().Be("invocation_completed");
        attributes.GetProperty("function.result").GetString().Should().Be("failed");
        attributes.GetProperty("duration_ms").GetDouble().Should().Be(7);
        attributes.GetProperty("trace_id").GetString().Should().Be("trace-1");
        attributes.GetProperty("span_id").GetString().Should().Be("span-1");
        attributes.GetProperty("parent_span_id").GetString().Should().Be("parent-1");
        stderr.ToString().Should().Be(string.Empty);
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
        attributes.GetProperty("cli.event_kind").GetString().Should().Be("host_state_changed");
        attributes.GetProperty("host.state").GetString().Should().Be("ready");
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
        document.RootElement.GetProperty("category").GetString().Should().Be("SystemTraceMiddleware");
        JsonElement attributes = document.RootElement.GetProperty("attributes");
        attributes.GetProperty("http.method").GetString().Should().Be("GET");
        attributes.GetProperty("http.target").GetString().Should().Be("http://localhost/api/HttpTrigger1");
        attributes.GetProperty("http.status_code").GetInt32().Should().Be(200);
        attributes.GetProperty("duration_ms").GetDouble().Should().Be(42.25);
    }
}
