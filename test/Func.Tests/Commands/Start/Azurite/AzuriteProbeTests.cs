// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Azure.Functions.Cli.Commands.Start.Azurite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Azure.Functions.Cli.Tests.Commands.Start.Azurite;

public class AzuriteProbeTests : IAsyncLifetime
{
    private readonly List<IDisposable> _disposables = [];
    private ServiceProvider? _services;

    public Task InitializeAsync()
    {
        var collection = new ServiceCollection();
        collection.AddAzuriteProbe();
        _services = collection.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in _disposables)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Best effort cleanup: a server may have already faulted.
            }
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }
    }

    [Fact]
    public async Task ProbeAsync_AllEndpointsReady_ViaRequestIdHeader_ReturnsReady()
    {
        var (blob, blobUri) = StartServer(static ctx =>
        {
            ctx.Response.Headers.Add("x-ms-request-id", "abc-123");
            ctx.Response.StatusCode = 400;
        });
        var (queue, queueUri) = StartServer(static ctx =>
        {
            ctx.Response.Headers.Add("x-ms-request-id", "def-456");
            ctx.Response.StatusCode = 403;
        });
        var (table, tableUri) = StartServer(static ctx =>
        {
            ctx.Response.Headers.Add("x-ms-request-id", "ghi-789");
            ctx.Response.StatusCode = 200;
        });
        _disposables.Add(blob);
        _disposables.Add(queue);
        _disposables.Add(table);

        var probe = _services!.GetRequiredService<IAzuriteProbe>();
        var endpoints = new AzuriteEndpointTuple(blobUri, queueUri, tableUri, "devstoreaccount1");

        var result = await probe.ProbeAsync(endpoints, CancellationToken.None);

        Assert.Equal(AzuriteProbeStatus.Ready, result.Status);
        Assert.All(result.Endpoints, o =>
            Assert.Equal(AzuriteEndpointStatus.Ready, o.Status));
        Assert.Contains(result.Endpoints, o => o.RequestId == "abc-123");
    }

    [Fact]
    public async Task ProbeAsync_EndpointReady_ViaErrorCodeHeader_IsRecognized()
    {
        var (server, uri) = StartServer(static ctx =>
        {
            ctx.Response.Headers.Add("x-ms-error-code", "AuthenticationFailed");
            ctx.Response.StatusCode = 403;
        });
        _disposables.Add(server);

        var outcome = await ProbeSingleAsync(uri);

        Assert.Equal(AzuriteEndpointStatus.Ready, outcome.Status);
        Assert.Equal("AuthenticationFailed", outcome.ErrorCode);
    }

    [Fact]
    public async Task ProbeAsync_EndpointReady_ViaAzuriteServerHeader_IsRecognized()
    {
        var (server, uri) = StartServer(static ctx =>
        {
            // HttpListener owns the Server header; allow user overrides.
            ctx.Response.Headers.Remove("Server");
            ctx.Response.Headers.Add("Server", "Azurite-Blob/3.35.0");
            ctx.Response.StatusCode = 200;
        });
        _disposables.Add(server);

        var outcome = await ProbeSingleAsync(uri);

        Assert.Equal(AzuriteEndpointStatus.Ready, outcome.Status);
    }

    [Fact]
    public async Task ProbeAsync_PlainHttpResponse_WithNoStorageHeaders_IsPortConflict()
    {
        var (blob, blobUri) = StartServer(WritePlainOk);
        var (queue, queueUri) = StartServer(WritePlainOk);
        var (table, tableUri) = StartServer(WritePlainOk);
        _disposables.Add(blob);
        _disposables.Add(queue);
        _disposables.Add(table);

        var probe = _services!.GetRequiredService<IAzuriteProbe>();
        var endpoints = new AzuriteEndpointTuple(blobUri, queueUri, tableUri, "devstoreaccount1");

        var result = await probe.ProbeAsync(endpoints, CancellationToken.None);

        Assert.Equal(AzuriteProbeStatus.PortConflict, result.Status);
        Assert.All(result.Endpoints, o =>
            Assert.Equal(AzuriteEndpointStatus.PortConflict, o.Status));
    }

    [Fact]
    public async Task ProbeAsync_AllEndpointsRefused_IsNotListening()
    {
        // Reserve and release three ports so connections are refused immediately.
        var blobUri = NewUriOnUnusedPort();
        var queueUri = NewUriOnUnusedPort();
        var tableUri = NewUriOnUnusedPort();

        var probe = _services!.GetRequiredService<IAzuriteProbe>();
        var endpoints = new AzuriteEndpointTuple(blobUri, queueUri, tableUri, "devstoreaccount1");

        var result = await probe.ProbeAsync(endpoints, CancellationToken.None);

        Assert.Equal(AzuriteProbeStatus.NotListening, result.Status);
        Assert.All(result.Endpoints, o =>
            Assert.Equal(AzuriteEndpointStatus.NotListening, o.Status));
    }

    [Fact]
    public async Task ProbeAsync_TwoReadyOneRefused_IsPartial()
    {
        var (blob, blobUri) = StartServer(static ctx =>
        {
            ctx.Response.Headers.Add("x-ms-request-id", "abc");
            ctx.Response.StatusCode = 400;
        });
        var (queue, queueUri) = StartServer(static ctx =>
        {
            ctx.Response.Headers.Add("x-ms-request-id", "def");
            ctx.Response.StatusCode = 400;
        });
        _disposables.Add(blob);
        _disposables.Add(queue);

        var tableUri = NewUriOnUnusedPort();

        var probe = _services!.GetRequiredService<IAzuriteProbe>();
        var endpoints = new AzuriteEndpointTuple(blobUri, queueUri, tableUri, "devstoreaccount1");

        var result = await probe.ProbeAsync(endpoints, CancellationToken.None);

        Assert.Equal(AzuriteProbeStatus.Partial, result.Status);

        var byService = result.Endpoints.ToDictionary(o => o.Service);
        Assert.Equal(AzuriteEndpointStatus.Ready, byService[AzuriteService.Blob].Status);
        Assert.Equal(AzuriteEndpointStatus.Ready, byService[AzuriteService.Queue].Status);
        Assert.Equal(AzuriteEndpointStatus.NotListening, byService[AzuriteService.Table].Status);
    }

    [Fact]
    public async Task ProbeAsync_NotListeningAndPortConflict_IsPartial()
    {
        // One endpoint refused + two non-storage responders: not "all PortConflict",
        // not "all NotListening", so must aggregate to Partial.
        var blobUri = NewUriOnUnusedPort();
        var (queue, queueUri) = StartServer(WritePlainOk);
        var (table, tableUri) = StartServer(WritePlainOk);
        _disposables.Add(queue);
        _disposables.Add(table);

        var probe = _services!.GetRequiredService<IAzuriteProbe>();
        var endpoints = new AzuriteEndpointTuple(blobUri, queueUri, tableUri, "devstoreaccount1");

        var result = await probe.ProbeAsync(endpoints, CancellationToken.None);

        Assert.Equal(AzuriteProbeStatus.Partial, result.Status);

        var byService = result.Endpoints.ToDictionary(o => o.Service);
        Assert.Equal(AzuriteEndpointStatus.NotListening, byService[AzuriteService.Blob].Status);
        Assert.Equal(AzuriteEndpointStatus.PortConflict, byService[AzuriteService.Queue].Status);
        Assert.Equal(AzuriteEndpointStatus.PortConflict, byService[AzuriteService.Table].Status);
    }

    [Fact]
    public async Task ProbeAsync_AlreadyCancelledToken_Throws()
    {
        var blobUri = NewUriOnUnusedPort();
        var queueUri = NewUriOnUnusedPort();
        var tableUri = NewUriOnUnusedPort();

        var probe = _services!.GetRequiredService<IAzuriteProbe>();
        var endpoints = new AzuriteEndpointTuple(blobUri, queueUri, tableUri, "devstoreaccount1");

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.ProbeAsync(endpoints, cts.Token));
    }

    private async Task<AzuriteEndpointProbeOutcome> ProbeSingleAsync(Uri uri)
    {
        // Use the live endpoint for one service and free ports for the other two
        // so the per-endpoint outcome is exercised directly.
        var others1 = NewUriOnUnusedPort();
        var others2 = NewUriOnUnusedPort();

        var probe = _services!.GetRequiredService<IAzuriteProbe>();
        var endpoints = new AzuriteEndpointTuple(uri, others1, others2, "devstoreaccount1");

        var result = await probe.ProbeAsync(endpoints, CancellationToken.None);
        return result.Endpoints.First(o => o.Service == AzuriteService.Blob);
    }

    private static void WritePlainOk(HttpListenerContext ctx)
    {
        ctx.Response.StatusCode = 200;
        var bytes = Encoding.UTF8.GetBytes("hello from a non-storage server");
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    private static (TestHttpServer Server, Uri Endpoint) StartServer(Action<HttpListenerContext> handler)
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        var server = new TestHttpServer(prefix, handler);
        server.Start();
        // Use the path-style endpoint shape the classifier produces.
        var endpoint = new Uri($"http://127.0.0.1:{port}/devstoreaccount1");
        return (server, endpoint);
    }

    private static Uri NewUriOnUnusedPort()
    {
        var port = GetFreePort();
        return new Uri($"http://127.0.0.1:{port}/devstoreaccount1");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class TestHttpServer(string prefix, Action<HttpListenerContext> handler) : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public void Start()
        {
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _loop = Task.Run(LoopAsync);
        }

        private async Task LoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                try
                {
                    handler(ctx);
                }
                catch
                {
                    // Tests assert via probe output; suppress handler faults.
                }
                finally
                {
                    try
                    {
                        ctx.Response.Close();
                    }
                    catch
                    {
                        // Connection already closed.
                    }
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                _listener.Stop();
            }
            catch
            {
                // Already stopped.
            }

            try
            {
                _loop?.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Loop already exited.
            }

            ((IDisposable)_listener).Dispose();
            _cts.Dispose();
        }
    }
}
