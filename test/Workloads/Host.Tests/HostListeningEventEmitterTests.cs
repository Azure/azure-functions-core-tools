// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using NSubstitute;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class HostListeningEventEmitterTests
{
    [Theory]
    [InlineData("http://0.0.0.0:49152", "http://localhost:49152/")]
    [InlineData("http://[::]:49153", "http://localhost:49153/")]
    [InlineData("http://127.0.0.1:7071", "http://localhost:7071/")]
    [InlineData("http://[::1]:49154", "http://localhost:49154/")]
    [InlineData("http://127.0.0.2:49155", "http://127.0.0.2:49155/")]
    [InlineData("http://192.0.2.1:7071", "http://192.0.2.1:7071/")]
    public void EmitSnapshot_ReportsActualBoundAddress(string address, string expected)
    {
        IServer server = CreateServer(address);
        var emitter = new HostListeningEventEmitter(new BoundHostAddressReader(server));
        using var writer = new StringWriter();

        emitter.EmitSnapshot(writer);

        using var document = JsonDocument.Parse(writer.ToString());
        JsonElement root = document.RootElement;
        root.GetProperty("event_id").GetProperty("name").GetString().Should().Be("HostListening");
        root.GetProperty("attributes").GetProperty("host.listen_uri").GetString().Should().Be(expected);
        root.GetProperty("message").GetString().Should().Be($"Listening on {expected}");
    }

    [Fact]
    public void Constructor_NullAddressReader_ThrowsArgumentNullException()
    {
        Func<HostListeningEventEmitter> act = () => new HostListeningEventEmitter(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("addressReader");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://0.0.0.0:0")]
    public void EmitSnapshot_WithoutResolvedAddress_ThrowsInvalidOperationException(string? address)
    {
        var emitter = new HostListeningEventEmitter(new BoundHostAddressReader(CreateServer(address)));

        Action act = () => emitter.EmitSnapshot();

        act.Should().Throw<InvalidOperationException>();
    }

    private static IServer CreateServer(string? address)
    {
        var features = new FeatureCollection();
        var addresses = new ServerAddressesFeature();
        if (address is not null)
        {
            addresses.Addresses.Add(address);
        }

        features.Set<IServerAddressesFeature>(addresses);
        IServer server = Substitute.For<IServer>();
        server.Features.Returns(features);
        return server;
    }
}
