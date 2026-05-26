// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Represents the CDN manifest envelope: <c>{ "templates": [...] }</c>.
/// </summary>
internal sealed record QuickstartManifestEnvelope
{
    [JsonPropertyName("templates")]
    public List<QuickstartEntry> Templates { get; init; } = [];
}

[JsonSerializable(typeof(QuickstartManifestEnvelope))]
[JsonSerializable(typeof(QuickstartManifest))]
[JsonSerializable(typeof(QuickstartEntry))]
[JsonSerializable(typeof(List<QuickstartEntry>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class QuickstartManifestJsonContext : JsonSerializerContext
{
}
