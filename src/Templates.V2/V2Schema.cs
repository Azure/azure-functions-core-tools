// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// JSON DTOs for the v2 templates schema (templates-workload-spec.md §5.4).
/// One <see cref="NewTemplate"/> per entry in
/// <c>tools/any/content/v2/templates/templates.json</c>. Inline files map
/// stores the per-template payload contents.
/// </summary>
internal sealed class NewTemplate
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("programmingModel")]
    public string? ProgrammingModel { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("triggerType")]
    public string? TriggerType { get; set; }

    [JsonPropertyName("category")]
    public List<string>? Category { get; set; }

    [JsonPropertyName("categoryStyle")]
    public string? CategoryStyle { get; set; }

    [JsonPropertyName("jobs")]
    public List<V2Job>? Jobs { get; set; }

    [JsonPropertyName("actions")]
    public List<V2Action>? Actions { get; set; }

    [JsonPropertyName("files")]
    public Dictionary<string, string>? Files { get; set; }
}

internal sealed class V2Job
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("inputs")]
    public List<V2Input>? Inputs { get; set; }
}

internal sealed class V2Input
{
    [JsonPropertyName("assignTo")]
    public string? AssignTo { get; set; }

    [JsonPropertyName("paramId")]
    public string? ParamId { get; set; }

    [JsonPropertyName("defaultValue")]
    [JsonConverter(typeof(PermissiveStringConverter))]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

internal sealed class V2Action
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    [JsonPropertyName("fileContent")]
    public string? FileContent { get; set; }

    [JsonPropertyName("assignTo")]
    public string? AssignTo { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("continueOnError")]
    public bool ContinueOnError { get; set; }
}

/// <summary>
/// A single entry from <c>tools/any/content/v2/bindings/userPrompts.json</c>.
/// </summary>
internal sealed class UserPromptDoc
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("help")]
    public string? Help { get; set; }

    [JsonPropertyName("placeholder")]
    public string? Placeholder { get; set; }

    [JsonPropertyName("defaultValue")]
    [JsonConverter(typeof(PermissiveStringConverter))]
    public string? DefaultValue { get; set; }

    [JsonPropertyName("required")]
    public bool? Required { get; set; }

    [JsonPropertyName("validators")]
    public List<UserPromptValidator>? Validators { get; set; }

    [JsonPropertyName("enum")]
    public List<UserPromptEnumEntry>? Enum { get; set; }
}

internal sealed class UserPromptValidator
{
    [JsonPropertyName("expression")]
    public string? Expression { get; set; }

    [JsonPropertyName("errorText")]
    public string? ErrorText { get; set; }
}

internal sealed class UserPromptEnumEntry
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }
}
