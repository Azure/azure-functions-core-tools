// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// A single entry from the CDN quickstart manifest.
/// </summary>
internal sealed record QuickstartEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;

    [JsonPropertyName("resource")]
    public string Resource { get; init; } = string.Empty;

    [JsonPropertyName("iac")]
    public string? Iac { get; init; }

    [JsonPropertyName("repositoryUrl")]
    public string RepositoryUrl { get; init; } = string.Empty;

    [JsonPropertyName("folderPath")]
    public string FolderPath { get; init; } = ".";

    /// <summary>
    /// Optional git ref (tag, branch, or commit SHA) to check out from the repository.
    /// When present, this is preferred over <c>HEAD</c>.
    /// </summary>
    [JsonPropertyName("gitRef")]
    public string? GitRef { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    [JsonPropertyName("shortDescription")]
    public string? ShortDescription { get; init; }

    [JsonPropertyName("whatsIncluded")]
    public IReadOnlyList<string> WhatIsIncluded { get; init; } = [];

    [JsonPropertyName("longDescription")]
    public string? LongDescription { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("categories")]
    public IReadOnlyList<string> Categories { get; init; } = [];

    [JsonPropertyName("priority")]
    public int Priority { get; init; }
}
