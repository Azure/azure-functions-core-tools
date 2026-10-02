// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Azure.Core.Pipeline;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Telemetry;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Azure.Functions.Cli.Tests.Telemetry;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryExportCollection
{
    public const string Name = "Telemetry export";
}

[Collection(TelemetryExportCollection.Name)]
public class TelemetryExportTests
{
    [Fact]
    public void AzureMonitorExport_ContainsEnrichedSignalsWithoutResourceOrSdkMetrics()
    {
        using var environment = new CliTelemetryEnvironment(new ProcessEnvironment(), Environment.SetEnvironmentVariable);
        environment.Apply();
        var payloads = new ConcurrentQueue<string>();
        using var client = new HttpClient(new RecordingHandler(payloads));
        var options = new AzureMonitorExporterOptions();
        CliTelemetry.ConfigureExporter(options, "InstrumentationKey=11111111-1111-1111-1111-111111111111");
        options.Transport = new HttpClientTransport(client);
        options.DisableOfflineStorage = true;

        using var traces = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(CliTelemetry.CreateResourceBuilder())
            .AddSource(CliTelemetry.SourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddProcessor(new CliActivityEnrichmentProcessor(CliTelemetry.GetCommonAttributes()))
            .AddProcessor(new SimpleActivityExportProcessor(new AzureMonitorTraceExporter(options)))
            .Build();
        using var metrics = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(CliTelemetry.CreateResourceBuilder())
            .AddMeter(CliTelemetry.SourceName)
            .AddReader(new PeriodicExportingMetricReader(new AzureMonitorMetricExporter(options)))
            .Build();
        var startTime = DateTimeOffset.UtcNow.AddMilliseconds(-30);

        using (Activity? command = CliTelemetry.Trace.StartCommandActivity(startTime))
        {
            command.Should().NotBeNull();
            command!.Recorded.Should().BeTrue();
            command.SetCommandName("export-shape-test");
            var boot = new WorkloadBootTelemetry(2, startTime.AddMilliseconds(1), TimeSpan.FromMilliseconds(10));
            CliTelemetry.Trace.RecordWorkloadBootActivity(boot);
            CliTelemetry.Metric.RecordWorkloadBoot(boot.WorkloadCount, (long)boot.Duration.TotalMilliseconds);
            CliTelemetry.Metric.RecordCommand("export-shape-test", exitCode: 0, durationMs: 30);
        }

        traces.ForceFlush().Should().BeTrue();
        metrics.ForceFlush().Should().BeTrue();

        List<JsonElement> items = [.. payloads.SelectMany(ParseItems)];
        var spans = items.Where(item => item.GetProperty("data").GetProperty("baseType").GetString() == "RemoteDependencyData").ToList();
        var metricItems = items.Where(item => item.GetProperty("data").GetProperty("baseType").GetString() == "MetricData").ToList();
        spans.Should().HaveCount(2);
        metricItems.Should().HaveCount(3);
        items.Should().HaveCount(5);

        foreach (JsonElement item in items)
        {
            JsonElement properties = item.GetProperty("data").GetProperty("baseData").GetProperty("properties");
            foreach (var attribute in CliTelemetry.GetCommonAttributes())
            {
                properties.GetProperty(attribute.Key).GetString().Should().Be(attribute.Value.ToString());
            }

            item.GetProperty("tags").GetProperty("ai.cloud.role").GetString().Should().Be(CliTelemetry.SourceName);
        }

        var metricNames = metricItems.SelectMany(item =>
            item.GetProperty("data").GetProperty("baseData").GetProperty("metrics").EnumerateArray()
                .Select(metric => metric.GetProperty("name").GetString()));
        metricNames.Should().BeEquivalentTo(
            TelemetryConventions.CommandCountInstrument,
            TelemetryConventions.CommandDurationInstrument,
            TelemetryConventions.WorkloadBootDurationInstrument);
    }

    private static IEnumerable<JsonElement> ParseItems(string payload)
    {
        if (payload.TrimStart().StartsWith('['))
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
        }

        return payload.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.Clone();
        }).ToArray();
    }

    private sealed class RecordingHandler(ConcurrentQueue<string> payloads) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Stream stream = request.Content!.ReadAsStream(cancellationToken);
            if (request.Content.Headers.ContentEncoding.Contains("gzip"))
            {
                stream = new GZipStream(stream, CompressionMode.Decompress);
            }

            using var reader = new StreamReader(stream);
            payloads.Enqueue(reader.ReadToEnd());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Send(request, cancellationToken));
    }
}
