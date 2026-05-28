// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite;

/// <summary>
/// The set of Azurite service endpoints inferred from an
/// <c>AzureWebJobsStorage</c> connection string.
/// </summary>
internal sealed record AzuriteEndpointTuple(Uri BlobEndpoint, Uri QueueEndpoint, Uri TableEndpoint, string AccountName);
