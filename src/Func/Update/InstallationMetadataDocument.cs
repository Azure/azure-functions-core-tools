// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Update;

internal sealed class InstallationMetadataDocument
{
    [JsonPropertyName("schemaVersion")]
    [JsonRequired]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("installMethod")]
    [JsonRequired]
    public string InstallMethod { get; init; } = string.Empty;

    [JsonPropertyName("updatePolicy")]
    [JsonRequired]
    public string UpdatePolicy { get; init; } = string.Empty;
}
