// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NuGet.Packaging;

namespace Azure.Functions.Cli.Tests.Templates;

/// <summary>
/// Serves one unchanged repository-built package over a private loopback NuGet v3 feed.
/// </summary>
internal sealed class PublicWorkloadFeed : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly byte[] _package;
    private readonly string _id;
    private readonly string _version;
    private readonly Task _server;

    public PublicWorkloadFeed(string archive)
    {
        using PackageArchiveReader package = new(archive);
        _id = package.NuspecReader.GetId().ToLowerInvariant();
        _version = package.NuspecReader.GetVersion().ToNormalizedString().ToLowerInvariant();
        _package = File.ReadAllBytes(archive);
        _listener.Start();
        string root = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        Index = root + "v3/index.json";
        FlatContainer = root + "flat/";
        _server = ServeAsync(_cancellation.Token);
    }

    public string Index { get; }

    private string FlatContainer { get; }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                await using NetworkStream stream = client.GetStream();
                using StreamReader reader = new(stream, Encoding.ASCII, false, leaveOpen: true);
                string? request = await reader.ReadLineAsync(cancellationToken);
                while (await reader.ReadLineAsync(cancellationToken) is { Length: > 0 })
                {
                }
                string? path = request?.Split(' ').ElementAtOrDefault(1);
                byte[]? response = path switch
                {
                    "/v3/index.json" => JsonSerializer.SerializeToUtf8Bytes(new
                    {
                        version = "3.0.0",
                        resources = new[] { new Dictionary<string, string> { ["@id"] = FlatContainer, ["@type"] = "PackageBaseAddress/3.0.0" } },
                    }),
                    _ when path == $"/flat/{_id}/index.json" => JsonSerializer.SerializeToUtf8Bytes(new { versions = new[] { _version } }),
                    _ when path == $"/flat/{_id}/{_version}/{_id}.{_version}.nupkg" => _package,
                    _ => null,
                };
                byte[] body = response ?? [];
                byte[] headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {(response is null ? "404 Not Found" : "200 OK")}\r\n"
                    + $"Content-Length: {body.Length}\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, cancellationToken);
                await stream.WriteAsync(body, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Disposing the fixture stops its owned listener and pending request.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _listener.Stop();
        await _server;
        _cancellation.Dispose();
    }
}
