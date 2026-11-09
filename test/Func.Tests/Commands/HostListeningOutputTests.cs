// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Start.Host;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Console.Theme;
using Azure.Functions.Cli.Hosting.Dashboard;
using Azure.Functions.Cli.Hosting.Dashboard.Rendering;
using Azure.Functions.Cli.Hosting.Events;
using Spectre.Console;

namespace Azure.Functions.Cli.Tests.Commands;

public sealed class HostListeningOutputTests
{
    [Theory]
    [InlineData(true, "localhost")]
    [InlineData(false, "localhost")]
    [InlineData(true, "127.0.0.2")]
    [InlineData(false, "127.0.0.2")]
    public async Task BoundAddress_ReachesJsonPlainAndDashboardState(bool readyBeforeBinding, string address)
    {
        var parser = new LineHostProcessOutputParser();
        var state = new DashboardState();
        using var plainOutput = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Out = new AnsiConsoleOutput(plainOutput),
        });
        console.Profile.Width = 240;
        await using var plain = new PlainRenderer(new SpectreInteractionService(new DefaultTheme(), console, console), console);
        using var jsonOutput = new MemoryStream();
        await using var json = new JsonRenderer(jsonOutput, ownsStream: false);
        const string ready = """
            {"source":"azure-functions-cli-host","schema_version":1,"message":"Host ready","attributes":{"host.state":"ready"}}
            """;
        string listenUri = $"http://{address}:49152/";
        string listening = $$$"""
            {"source":"azure-functions-cli-host","schema_version":1,"category":"Host.Process","level":"information","event_id":{"id":1001,"name":"HostListening"},"message":"Listening on {{{listenUri}}}","attributes":{"host.listen_uri":"{{{listenUri}}}"}}
            """;
        const string function = """
            {"source":"azure-functions-cli-host","schema_version":1,"message":"Function discovered","attributes":{"cli.event_kind":"function_discovered","function.name":"hello","function.trigger_type":"http","function.route":"/api/hello"}}
            """;
        string[] lines = readyBeforeBinding ? [ready, listening, function] : [listening, ready, function];

        foreach (string line in lines)
        {
            HostLogEntry entry = parser.ParseLine(HostProcessStreamNames.StandardOutput, line, DateTimeOffset.UnixEpoch);
            IReadOnlyList<DashboardEvent> events = state.Observe(entry);
            await plain.OnEventAsync(entry, events, CancellationToken.None);
            await json.OnEventAsync(entry, events, CancellationToken.None);
        }

        state.Snapshot().ListenUri.Should().Be(listenUri);
        plainOutput.ToString().Should().Contain($"Listening on {listenUri}")
            .And.Contain($"{listenUri}api/hello")
            .And.NotContain("localhost:0");
        string[] records = Encoding.UTF8.GetString(jsonOutput.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using var listeningRecord = JsonDocument.Parse(records.Single(line => line.Contains("\"event_name\":\"HostListening\"", StringComparison.Ordinal)));
        listeningRecord.RootElement.GetProperty("kind").GetString().Should().Be("log");
        listeningRecord.RootElement.GetProperty("attributes").GetProperty("host.listen_uri").GetString()
            .Should().Be(listenUri);
    }
}
