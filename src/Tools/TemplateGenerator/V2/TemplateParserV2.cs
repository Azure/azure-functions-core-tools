// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2;

internal static class TemplateParserV2
{
    public static EquatableArray<TemplateModelV2> Parse(string json, string? language)
    {
        using var doc = JsonDocument.Parse(json);
        ImmutableArray<TemplateModelV2>.Builder builder = ImmutableArray.CreateBuilder<TemplateModelV2>(doc.RootElement.GetArrayLength());

        string? suffix = string.IsNullOrEmpty(language) ? null : "-" + language;

        foreach (JsonElement element in doc.RootElement.EnumerateArray())
        {
            string id = element.GetProperty("id").GetString()!;

            if (suffix is not null && !id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Add(ParseTemplate(id, suffix, element));
        }

        return builder.ToImmutable();
    }

    private static TemplateModelV2 ParseTemplate(string id, string? suffix, JsonElement element)
    {
        string name = suffix is not null && id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? id.Substring(0, id.Length - suffix.Length)
            : id;

        return new()
        {
            Id = id,
            Name = name,
            DisplayName = element.GetProperty("name").GetString()!,
            Description = element.GetProperty("description").GetString()!,
            Author = element.GetProperty("author").GetString()!,
            ProgrammingModel = element.GetProperty("programmingModel").GetString()!,
            Language = element.GetProperty("language").GetString()!,
            Files = ParseFiles(element.GetProperty("files")),
            Jobs = ParseJobs(element.GetProperty("jobs")),
            Actions = ParseActions(element.GetProperty("actions")),
        };
    }

    private static EquatableArray<FileModelV2> ParseFiles(JsonElement element)
    {
        ImmutableArray<FileModelV2>.Builder builder = ImmutableArray.CreateBuilder<FileModelV2>();
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            builder.Add(new FileModelV2(prop.Name, prop.Value.GetString()!));
        }

        return builder.ToImmutable();
    }

    private static EquatableArray<TemplateJobModelV2> ParseJobs(JsonElement element)
    {
        ImmutableArray<TemplateJobModelV2>.Builder builder = ImmutableArray.CreateBuilder<TemplateJobModelV2>(element.GetArrayLength());
        foreach (JsonElement job in element.EnumerateArray())
        {
            builder.Add(ParseJob(job));
        }

        return builder.MoveToImmutable();
    }

    private static TemplateJobModelV2 ParseJob(JsonElement element)
    {
        return new()
        {
            Name = element.GetProperty("name").GetString()!,
            Type = element.GetProperty("type").GetString()!,
            Inputs = ParseInputs(element.GetProperty("inputs")),
            ActionRefs = GetStringArray(element.GetProperty("actions")),
        };
    }

    private static EquatableArray<TemplateInputModelV2> ParseInputs(JsonElement element)
    {
        ImmutableArray<TemplateInputModelV2>.Builder builder = ImmutableArray.CreateBuilder<TemplateInputModelV2>(element.GetArrayLength());
        foreach (JsonElement input in element.EnumerateArray())
        {
            builder.Add(ParseInput(input));
        }

        return builder.MoveToImmutable();
    }

    private static TemplateInputModelV2 ParseInput(JsonElement element)
    {
        TemplateInputConditionModelV2? condition = null;
        if (element.TryGetProperty("condition", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
        {
            condition = new()
            {
                Name = c.GetProperty("name").GetString()!,
                Values = GetStringArray(c.GetProperty("values")),
                Operator = c.GetProperty("operator").GetString()!,
            };
        }

        return new()
        {
            AssignTo = element.GetProperty("assignTo").GetString()!,
            ParamId = element.GetProperty("paramId").GetString()!,
            DefaultValue = GetOptionalString(element, "defaultValue"),
            Required = element.TryGetProperty("required", out JsonElement r) && r.ValueKind == JsonValueKind.True,
            Condition = condition,
        };
    }

    private static EquatableArray<TemplateActionModelV2> ParseActions(JsonElement element)
    {
        ImmutableArray<TemplateActionModelV2>.Builder builder = ImmutableArray.CreateBuilder<TemplateActionModelV2>(element.GetArrayLength());
        foreach (JsonElement action in element.EnumerateArray())
        {
            builder.Add(ParseAction(action));
        }

        return builder.MoveToImmutable();
    }

    private static TemplateActionModelV2 ParseAction(JsonElement e)
    {
        string name = e.GetProperty("name").GetString()!;
        string type = e.GetProperty("type").GetString()!;

        ImmutableArray<ActionExtensionEntryV2>.Builder extensions = ImmutableArray.CreateBuilder<ActionExtensionEntryV2>();
        foreach (JsonProperty prop in e.EnumerateObject())
        {
            if (prop.Name == "name" || prop.Name == "type")
            {
                continue;
            }

            ActionExtensionValueV2 value = prop.Value.ValueKind switch
            {
                JsonValueKind.True => new ActionExtensionValueV2(ActionExtensionValueKindV2.Bool, null, true, null, null),
                JsonValueKind.False => new ActionExtensionValueV2(ActionExtensionValueKindV2.Bool, null, false, null, null),
                JsonValueKind.Number => new ActionExtensionValueV2(ActionExtensionValueKindV2.Number, null, null, prop.Value.GetDouble(), null),
                JsonValueKind.Array => new ActionExtensionValueV2(ActionExtensionValueKindV2.StringArray, null, null, null, GetStringArray(prop.Value)),
                JsonValueKind.Null => new ActionExtensionValueV2(ActionExtensionValueKindV2.Null, null, null, null, null),
                _ => new ActionExtensionValueV2(ActionExtensionValueKindV2.String, prop.Value.GetString(), null, null, null),
            };

            extensions.Add(new ActionExtensionEntryV2(prop.Name, value));
        }

        return new TemplateActionModelV2
        {
            Name = name,
            Type = type,
            ExtensionData = extensions.ToImmutable(),
        };
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind != JsonValueKind.Null
            ? prop.GetString()
            : null;

    private static EquatableArray<string> GetStringArray(JsonElement element)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(element.GetArrayLength());
        foreach (JsonElement item in element.EnumerateArray())
        {
            builder.Add(item.GetString()!);
        }

        return builder.MoveToImmutable();
    }
}
