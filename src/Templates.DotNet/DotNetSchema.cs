// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Templates.DotNet;

/// <summary>
/// JSON DTOs for the <c>dotnet-templates.json</c> schema documented in
/// templates-workload-spec.md §5.3.1. Hydrated at workload pack time from
/// each upstream <c>template.json</c> + sibling <c>dotnetcli.host.json</c>.
/// </summary>
internal sealed class DotNetTemplatesIndex
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("sourcePackage")]
    public DotNetSourcePackage? SourcePackage { get; set; }

    [JsonPropertyName("templates")]
    public List<DotNetTemplateRecord>? Templates { get; set; }
}

internal sealed class DotNetSourcePackage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// One DotNet template record. Mirrors templates-workload-spec.md §5.3.1 — every
/// field needed by `func new --list` and `func new --template X --help` is
/// already projected at workload pack time so reads are fully offline.
/// </summary>
internal sealed class DotNetTemplateRecord
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("shortNames")]
    public List<string>? ShortNames { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("groupIdentity")]
    public string? GroupIdentity { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("classifications")]
    public List<string>? Classifications { get; set; }

    [JsonPropertyName("defaultName")]
    public string? DefaultName { get; set; }

    [JsonPropertyName("constraints")]
    public List<object>? Constraints { get; set; }

    [JsonPropertyName("parameters")]
    public List<DotNetParameter>? Parameters { get; set; }
}

internal sealed class DotNetParameter
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("dataType")]
    public string? DataType { get; set; }

    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("choices")]
    public List<DotNetChoice>? Choices { get; set; }

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("isHidden")]
    public bool IsHidden { get; set; }

    [JsonPropertyName("allowMultipleValues")]
    public bool AllowMultipleValues { get; set; }

    [JsonPropertyName("shortNameOverride")]
    public string? ShortNameOverride { get; set; }

    [JsonPropertyName("longNameOverride")]
    public string? LongNameOverride { get; set; }
}

internal sealed class DotNetChoice
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
