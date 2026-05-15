// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2;

internal static class UserPromptsParserV2
{
    public static UserPromptsCatalogModelV2 Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        ImmutableArray<UserPromptModelV2>.Builder builder = ImmutableArray.CreateBuilder<UserPromptModelV2>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement e in doc.RootElement.EnumerateArray())
            {
                builder.Add(ParsePrompt(e));
            }
        }

        return new UserPromptsCatalogModelV2 { Prompts = builder.ToImmutable() };
    }

    private static UserPromptModelV2 ParsePrompt(JsonElement e) => new()
    {
        Id = e.GetProperty("id").GetString()!,
        Name = GetOptionalString(e, "name") ?? e.GetProperty("id").GetString()!,
        Value = ParseValueKind(GetOptionalString(e, "value")),
        DefaultValue = ParseDefaultValue(e),
        Label = GetOptionalString(e, "label"),
        Help = GetOptionalString(e, "help"),
        Placeholder = GetOptionalString(e, "placeholder"),
        Resource = GetOptionalString(e, "resource"),
        FileExtension = GetOptionalString(e, "FileExtension"),
        Validators = ParseValidators(e),
        Enum = ParseEnum(e),
    };

    private static UserPromptValueKindV2 ParseValueKind(string? value) => value?.ToLowerInvariant() switch
    {
        null => UserPromptValueKindV2.None,
        "string" => UserPromptValueKindV2.String,
        "boolean" => UserPromptValueKindV2.Bool,
        "enum" => UserPromptValueKindV2.Enum,
        _ => UserPromptValueKindV2.String,
    };

    private static UserPromptDefaultValueModelV2? ParseDefaultValue(JsonElement e)
    {
        if (!e.TryGetProperty("defaultValue", out JsonElement v))
        {
            return null;
        }

        return v.ValueKind switch
        {
            JsonValueKind.String => new UserPromptDefaultValueModelV2(UserPromptDefaultValueKindV2.String, v.GetString(), null),
            JsonValueKind.True => new UserPromptDefaultValueModelV2(UserPromptDefaultValueKindV2.Bool, null, true),
            JsonValueKind.False => new UserPromptDefaultValueModelV2(UserPromptDefaultValueKindV2.Bool, null, false),
            _ => null,
        };
    }

    private static EquatableArray<UserPromptValidatorModelV2> ParseValidators(JsonElement e)
    {
        if (!e.TryGetProperty("validators", out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<UserPromptValidatorModelV2>.Empty;
        }

        ImmutableArray<UserPromptValidatorModelV2>.Builder builder = ImmutableArray.CreateBuilder<UserPromptValidatorModelV2>(el.GetArrayLength());
        foreach (JsonElement v in el.EnumerateArray())
        {
            builder.Add(new UserPromptValidatorModelV2(
                GetOptionalString(v, "expression") ?? string.Empty,
                GetOptionalString(v, "errorText") ?? string.Empty));
        }

        return builder.MoveToImmutable();
    }

    private static EquatableArray<UserPromptEnumValueModelV2> ParseEnum(JsonElement e)
    {
        if (!e.TryGetProperty("enum", out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<UserPromptEnumValueModelV2>.Empty;
        }

        ImmutableArray<UserPromptEnumValueModelV2>.Builder builder = ImmutableArray.CreateBuilder<UserPromptEnumValueModelV2>(el.GetArrayLength());
        foreach (JsonElement v in el.EnumerateArray())
        {
            builder.Add(new UserPromptEnumValueModelV2(
                GetOptionalString(v, "value") ?? string.Empty,
                GetOptionalString(v, "display") ?? string.Empty));
        }

        return builder.MoveToImmutable();
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
