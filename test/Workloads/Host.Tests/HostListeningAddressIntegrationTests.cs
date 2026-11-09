// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Workloads.Host.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Cli.Workloads.Host.Tests;

public sealed class HostListeningAddressIntegrationTests
{
    [Theory]
    [InlineData("127.0.0.1", "localhost")]
    [InlineData("0.0.0.0", "localhost")]
    [InlineData("127.0.0.2", "127.0.0.2")]
    public async Task EmitSnapshot_AfterBindingPortZero_ReportsReachableNonzeroPort(string address, string expectedHost)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using IHost host = new HostBuilder()
            .ConfigureWebHost(builder => builder
                .UseKestrel()
                .UseUrls($"http://{address}:0")
                .ConfigureServices(services =>
                {
                    services.AddSingleton<BoundHostAddressReader>();
                    services.AddSingleton<HostListeningEventEmitter>();
                })
                .Configure(app => app.Run(context => context.Response.WriteAsync("ephemeral-port", context.RequestAborted))))
            .Build();
        using var writer = new StringWriter();
        await host.StartAsync(cancellation.Token);

        try
        {
            host.Services.GetRequiredService<HostListeningEventEmitter>().EmitSnapshot(writer);

            using var document = JsonDocument.Parse(writer.ToString());
            var uri = new Uri(document.RootElement.GetProperty("attributes").GetProperty("host.listen_uri").GetString()!);
            uri.Host.Should().Be(expectedHost);
            uri.Port.Should().BeInRange(1, 65535);
            IServer server = host.Services.GetRequiredService<IServer>();
            string boundAddress = server.Features.Get<IServerAddressesFeature>()!.Addresses.Should().ContainSingle().Subject;
            new Uri(boundAddress).Host.Should().Be(address);
            using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
            (await client.GetStringAsync(uri, cancellation.Token)).Should().Be("ephemeral-port");
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }
}
