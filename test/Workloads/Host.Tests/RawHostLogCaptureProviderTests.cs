// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit;

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

        string line = Assert.Single(File.ReadAllLines(capturePath));
        using var document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;

        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(Environment.ProcessId, root.GetProperty("process_id").GetInt32());
        Assert.True(root.GetProperty("thread_id").GetInt32() > 0);
        Assert.Equal("Function.HttpTrigger1", root.GetProperty("category").GetString());
        Assert.Equal("warning", root.GetProperty("level").GetString());
        Assert.Equal("Processing HttpTrigger1 took 123ms", root.GetProperty("message").GetString());

        JsonElement eventId = root.GetProperty("event_id");
        Assert.Equal(42, eventId.GetProperty("id").GetInt32());
        Assert.Equal("FunctionWarning", eventId.GetProperty("name").GetString());

        JsonElement state = root.GetProperty("state");
        Assert.Equal("HttpTrigger1", state.GetProperty("Name").GetString());
        Assert.Equal(123, state.GetProperty("ElapsedMs").GetInt32());
        Assert.Equal("Processing {Name} took {ElapsedMs}ms", state.GetProperty("{OriginalFormat}").GetString());

        JsonElement exception = root.GetProperty("exception");
        Assert.Equal(typeof(InvalidOperationException).FullName, exception.GetProperty("type").GetString());
        Assert.Equal("Something failed", exception.GetProperty("message").GetString());

        JsonElement capturedScope = Assert.Single(root.GetProperty("scopes").EnumerateArray());
        Assert.Equal("ScopeValue", capturedScope.GetProperty("values").GetProperty("ScopeKey").GetString());
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

        string line = Assert.Single(File.ReadAllLines(capturePath));
        using var document = JsonDocument.Parse(line);
        JsonElement capturedScope = Assert.Single(document.RootElement.GetProperty("scopes").EnumerateArray());
        Assert.Equal("abc123", capturedScope.GetProperty("values").GetProperty("InvocationId").GetString());
    }
}
