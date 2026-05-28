// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// Result of classifying an <c>AzureWebJobsStorage</c> connection string.
/// </summary>
internal sealed record AzureWebJobsStorageReference
{
    public required AzureWebJobsStorageClassification Classification { get; init; }

    /// <summary>
    /// Endpoints inferred from the connection string when the value enumerates
    /// explicit Blob, Queue, and Table endpoints. Null for the
    /// <c>UseDevelopmentStorage=true</c> shorthand (the caller fills in the
    /// defaults) and for non-local values.
    /// </summary>
    public AzuriteEndpointTuple? Endpoints { get; init; }

    /// <summary>
    /// Short human-readable explanation of the classification, suitable for
    /// verbose logging and diagnostics.
    /// </summary>
    public required string Reason { get; init; }

    public static AzureWebJobsStorageReference NotLocal(string reason) => new()
    {
        Classification = AzureWebJobsStorageClassification.NotLocal,
        Reason = reason,
    };

    public static AzureWebJobsStorageReference Manageable(AzuriteEndpointTuple? endpoints, string reason) => new()
    {
        Classification = AzureWebJobsStorageClassification.ManageableAzurite,
        Endpoints = endpoints,
        Reason = reason,
    };

    public static AzureWebJobsStorageReference UserConfigured(AzuriteEndpointTuple? endpoints, string reason) => new()
    {
        Classification = AzureWebJobsStorageClassification.UserConfiguredAzurite,
        Endpoints = endpoints,
        Reason = reason,
    };
}
