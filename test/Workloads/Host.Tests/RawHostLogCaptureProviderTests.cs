// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class RawHostLogCaptureProviderTests
{
    [Fact]
    public void Log_WritesRawStructuredRecord()
    {
        string capturePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "host-raw.ndjson");

        using (var provider = new RawHostLogCaptureProvider(capturePath))
        {
            ILogger logger = provider.CreateLogger("Function.HttpTrigger1");
            using IDisposable? loggingScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["ScopeKey"] = "ScopeValue",
            });

            logger.LogWarning(
                new EventId(42, "FunctionWarning"),
                new InvalidOperationException("Something failed"),
                "Processing {Name} took {ElapsedMs}ms",
                "HttpTrigger1",
                123);
        }

        string line = File.ReadAllLines(capturePath).Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        root.GetProperty("schema_version").GetInt32().Should().Be(1);
        root.GetProperty("process_id").GetInt32().Should().Be(Environment.ProcessId);
        (root.GetProperty("thread_id").GetInt32() > 0).Should().BeTrue();
        root.GetProperty("category").GetString().Should().Be("Function.HttpTrigger1");
        root.GetProperty("level").GetString().Should().Be("warning");
        root.GetProperty("message").GetString().Should().Be("Processing HttpTrigger1 took 123ms");

        JsonElement eventId = root.GetProperty("event_id");
        eventId.GetProperty("id").GetInt32().Should().Be(42);
        eventId.GetProperty("name").GetString().Should().Be("FunctionWarning");

        JsonElement state = root.GetProperty("state");
        state.GetProperty("Name").GetString().Should().Be("HttpTrigger1");
        state.GetProperty("ElapsedMs").GetInt32().Should().Be(123);
        state.GetProperty("{OriginalFormat}").GetString().Should().Be("Processing {Name} took {ElapsedMs}ms");

        JsonElement exception = root.GetProperty("exception");
        exception.GetProperty("type").GetString().Should().Be(typeof(InvalidOperationException).FullName);
        exception.GetProperty("message").GetString().Should().Be("Something failed");

        JsonElement capturedScope = root.GetProperty("scopes").EnumerateArray().Should().ContainSingle().Subject;
        capturedScope.GetProperty("values").GetProperty("ScopeKey").GetString().Should().Be("ScopeValue");
    }

    [Fact]
    public void Log_CapturesLoggerFactoryScopes()
    {
        string capturePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName(), "host-raw.ndjson");

        {
            using var provider = new RawHostLogCaptureProvider(capturePath);
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddProvider(provider);
            });

            ILogger logger = loggerFactory.CreateLogger("Host.Startup");
            using IDisposable? loggingScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["InvocationId"] = "abc123",
            });

            logger.LogInformation(new EventId(7, "HostStarted"), "Host started");
        }

        string line = File.ReadAllLines(capturePath).Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(line);
        JsonElement capturedScope = document.RootElement.GetProperty("scopes").EnumerateArray().Should().ContainSingle().Subject;
        capturedScope.GetProperty("values").GetProperty("InvocationId").GetString().Should().Be("abc123");
    }
}
