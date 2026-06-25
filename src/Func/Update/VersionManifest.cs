// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// JSON shape of the CDN version manifest at
/// <c>https://cdn.functions.azure.com/public/cli/v5/version.json</c>.
/// Contains the latest stable and preview versions published to the feed.
/// </summary>
internal sealed record VersionManifest(
    [property: JsonPropertyName("stable")] string? Stable,
    [property: JsonPropertyName("preview")] string? Preview);
