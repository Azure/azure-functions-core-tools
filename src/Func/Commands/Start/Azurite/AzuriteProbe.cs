// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <inheritdoc cref="IAzuriteProbe" />
internal sealed class AzuriteProbe(IHttpClientFactory httpClientFactory, ILogger<AzuriteProbe> logger) : IAzuriteProbe
{
    /// <summary>
    /// Name of the <see cref="HttpClient"/> registered in DI for the probe.
    /// </summary>
    internal const string HttpClientName = "AzuriteProbe";

    /// <summary>
    /// Storage REST API version sent with every probe request.
    /// </summary>
    internal const string StorageApiVersion = "2021-12-02";

    /// <summary>
    /// Per-request timeout. Short by design so an unresponsive port does not
    /// stall the overall probe.
    /// </summary>
    internal static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum number of bytes inspected from a response body when looking for
    /// an Azure Storage error payload.
    /// </summary>
    private const int MaxBodyInspectBytes = 8 * 1024;

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    private readonly ILogger<AzuriteProbe> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AzuriteProbeResult> ProbeAsync(AzuriteEndpointTuple endpoints, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        Task<AzuriteEndpointProbeOutcome> blobTask = ProbeServiceAsync(
            AzuriteService.Blob,
            BuildBlobProbeUri(endpoints.BlobEndpoint),
            endpoints.BlobEndpoint,
            cancellationToken);

        Task<AzuriteEndpointProbeOutcome> queueTask = ProbeServiceAsync(
            AzuriteService.Queue,
            BuildQueueProbeUri(endpoints.QueueEndpoint),
            endpoints.QueueEndpoint,
            cancellationToken);

        Task<AzuriteEndpointProbeOutcome> tableTask = ProbeServiceAsync(
            AzuriteService.Table,
            BuildTableProbeUri(endpoints.TableEndpoint),
            endpoints.TableEndpoint,
            cancellationToken);

        AzuriteEndpointProbeOutcome[] outcomes = await Task.WhenAll(blobTask, queueTask, tableTask)
            .ConfigureAwait(false);

        var result = AzuriteProbeResult.From(outcomes);
        _logger.LogDebug(
            "Azurite probe completed with status {ProbeStatus}: {ProbeReason}",
            result.Status,
            result.Reason);
        return result;
    }

    internal static Uri BuildBlobProbeUri(Uri blob) => AppendQuery(blob, "comp=list");

    internal static Uri BuildQueueProbeUri(Uri queue) => AppendQuery(queue, "comp=list");

    internal static Uri BuildTableProbeUri(Uri table)
    {
        UriBuilder builder = new(table);
        string path = builder.Path;
        if (!path.EndsWith('/'))
        {
            path += "/";
        }

        builder.Path = path + "Tables";
        builder.Query = string.Empty;
        return builder.Uri;
    }

    private static Uri AppendQuery(Uri endpoint, string query)
    {
        UriBuilder builder = new(endpoint);
        if (string.IsNullOrEmpty(builder.Query))
        {
            builder.Query = query;
        }
        else
        {
            builder.Query = builder.Query.TrimStart('?') + "&" + query;
        }

        return builder.Uri;
    }

    private async Task<AzuriteEndpointProbeOutcome> ProbeServiceAsync(
        AzuriteService service,
        Uri probeUri,
        Uri originalEndpoint,
        CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

        using HttpRequestMessage request = new(HttpMethod.Get, probeUri);
        request.Headers.TryAddWithoutValidation("x-ms-version", StorageApiVersion);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(PerRequestTimeout);

        try
        {
            using HttpResponseMessage response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            string? requestId = TryGetSingleHeader(response.Headers, "x-ms-request-id");
            string? errorCode = TryGetSingleHeader(response.Headers, "x-ms-error-code");
            bool serverIsAzurite = ServerHeaderContainsAzurite(response.Headers.Server);

            bool storageShaped = requestId is not null || errorCode is not null || serverIsAzurite;

            if (!storageShaped)
            {
                storageShaped = await BodyLooksLikeStorageErrorAsync(response, timeoutCts.Token)
                    .ConfigureAwait(false);
            }

            AzuriteEndpointStatus status = storageShaped
                ? AzuriteEndpointStatus.Ready
                : AzuriteEndpointStatus.PortConflict;

            _logger.LogDebug(
                "Probed {Service} endpoint {Endpoint}: HTTP {StatusCode}, ready={Ready}",
                service,
                originalEndpoint,
                (int)response.StatusCode,
                storageShaped);

            return new AzuriteEndpointProbeOutcome(
                service,
                originalEndpoint,
                status,
                HttpStatusCode: (int)response.StatusCode,
                RequestId: requestId,
                ErrorCode: errorCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "Probe for {Service} endpoint {Endpoint} timed out after {TimeoutSeconds}s",
                service,
                originalEndpoint,
                PerRequestTimeout.TotalSeconds);
            return new AzuriteEndpointProbeOutcome(
                service,
                originalEndpoint,
                AzuriteEndpointStatus.NotListening,
                Detail: $"Probe timed out after {PerRequestTimeout.TotalSeconds:F0}s.");
        }
        catch (HttpRequestException ex) when (IsConnectionRefused(ex))
        {
            _logger.LogDebug(
                ex,
                "Probe for {Service} endpoint {Endpoint} was refused",
                service,
                originalEndpoint);
            return new AzuriteEndpointProbeOutcome(
                service,
                originalEndpoint,
                AzuriteEndpointStatus.NotListening,
                Detail: ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(
                ex,
                "Probe for {Service} endpoint {Endpoint} failed",
                service,
                originalEndpoint);
            return new AzuriteEndpointProbeOutcome(
                service,
                originalEndpoint,
                AzuriteEndpointStatus.NotListening,
                Detail: ex.Message);
        }
    }

    private static bool IsConnectionRefused(HttpRequestException ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException sock)
            {
                return sock.SocketErrorCode is SocketError.ConnectionRefused
                    or SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable
                    or SocketError.HostNotFound
                    or SocketError.TryAgain;
            }
        }

        return false;
    }

    private static string? TryGetSingleHeader(HttpResponseHeaders headers, string name)
    {
        if (headers.TryGetValues(name, out IEnumerable<string>? values))
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static bool ServerHeaderContainsAzurite(HttpHeaderValueCollection<ProductInfoHeaderValue> server)
    {
        foreach (ProductInfoHeaderValue value in server)
        {
            if (value.Product is { } product &&
                product.Name.Contains("Azurite", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> BodyLooksLikeStorageErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return false;
        }

        string body;
        try
        {
            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            byte[] buffer = new byte[MaxBodyInspectBytes];
            int read = await ReadUpToAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                return false;
            }

            body = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Body inspection is best-effort: if we cannot read it, fall back to
            // header-only signals (already checked by the caller).
            return false;
        }

        string trimmed = body.TrimStart();
        if (trimmed.StartsWith('<'))
        {
            return XmlLooksLikeStorageError(trimmed);
        }

        if (trimmed.StartsWith('{'))
        {
            return JsonLooksLikeStorageError(trimmed);
        }

        return false;
    }

    private static async Task<int> ReadUpToAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool XmlLooksLikeStorageError(string body)
    {
        try
        {
            var doc = XDocument.Parse(body);
            XElement? root = doc.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "Error", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (XElement child in root.Elements())
            {
                if (string.Equals(child.Name.LocalName, "Code", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static bool JsonLooksLikeStorageError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("error", out JsonElement error) ||
                error.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return error.TryGetProperty("code", out JsonElement code) &&
                code.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
