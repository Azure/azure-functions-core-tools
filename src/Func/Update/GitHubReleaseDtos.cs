// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Update;

/// <summary>
/// JSON shape returned by
/// <c>GET https://api.github.com/repos/&lt;owner&gt;/&lt;repo&gt;/releases</c>.
/// Only the fields the update pipeline consumes are modelled.
/// </summary>
internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("prerelease")] bool IsPrerelease,
    [property: JsonPropertyName("assets")] IReadOnlyList<GitHubAsset> Assets);

/// <summary>
/// A downloadable artifact attached to a <see cref="GitHubRelease"/>.
/// </summary>
internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string DownloadUrl,
    [property: JsonPropertyName("size")] long Size);
