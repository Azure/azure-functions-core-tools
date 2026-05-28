// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Identifies the Azurite service an endpoint was probed for.
/// </summary>
internal enum AzuriteService
{
    Blob,
    Queue,
    Table,
}

/// <summary>
/// Per-endpoint probe classification. Mirrors <see cref="AzuriteProbeStatus"/>
/// minus <see cref="AzuriteProbeStatus.Partial"/>, which only makes sense as
/// an aggregate.
/// </summary>
internal enum AzuriteEndpointStatus
{
    /// <summary>
    /// The endpoint responded with markers that identify it as Azure Storage
    /// (Azurite or otherwise): an <c>x-ms-request-id</c> or
    /// <c>x-ms-error-code</c> header, a <c>Server</c> header that contains
    /// "Azurite", or a storage-style error payload.
    /// </summary>
    Ready,

    /// <summary>
    /// The TCP connection was refused or otherwise unreachable.
    /// </summary>
    NotListening,

    /// <summary>
    /// The endpoint responded, but the response did not look like Azure
    /// Storage. Something else is listening on the port.
    /// </summary>
    PortConflict,
}

/// <summary>
/// Result of probing a single Azurite service endpoint.
/// </summary>
/// <param name="Service">Service the endpoint hosts.</param>
/// <param name="Endpoint">Endpoint URI that was probed.</param>
/// <param name="Status">Classification of the response.</param>
/// <param name="HttpStatusCode">HTTP status code, when the endpoint responded.</param>
/// <param name="RequestId"><c>x-ms-request-id</c> header value, when present.</param>
/// <param name="ErrorCode"><c>x-ms-error-code</c> header value, when present.</param>
/// <param name="Detail">Optional human-readable diagnostic detail.</param>
internal sealed record AzuriteEndpointProbeOutcome(
    AzuriteService Service,
    Uri Endpoint,
    AzuriteEndpointStatus Status,
    int? HttpStatusCode = null,
    string? RequestId = null,
    string? ErrorCode = null,
    string? Detail = null);
