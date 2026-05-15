// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model.V1;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1;

internal static class TemplateParserV1
{
    public static EquatableArray<TemplateModelV1> Parse(string json, string? language)
    {
        using var doc = JsonDocument.Parse(json);
        ImmutableArray<TemplateModelV1>.Builder builder = ImmutableArray.CreateBuilder<TemplateModelV1>(doc.RootElement.GetArrayLength());

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

    private static TemplateModelV1 ParseTemplate(string id, string? suffix, JsonElement element)
    {
        string name = suffix is not null && id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? id.Substring(0, id.Length - suffix.Length)
            : id;

        return new()
        {
            Id = id,
            Name = name,
            Runtime = element.GetProperty("runtime").GetString()!,
            Files = ParseFiles(element.GetProperty("files")),
            Function = ParseFunction(element.GetProperty("function")),
            Metadata = ParseMetadata(element.GetProperty("metadata")),
        };
    }

    private static EquatableArray<FileModelV1> ParseFiles(JsonElement element)
    {
        ImmutableArray<FileModelV1>.Builder builder = ImmutableArray.CreateBuilder<FileModelV1>();
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            builder.Add(new FileModelV1(prop.Name, prop.Value.GetString()!));
        }

        return builder.ToImmutable();
    }

    private static FunctionModelV1 ParseFunction(JsonElement element) => new()
    {
        Bindings = ParseBindings(element.GetProperty("bindings")),
        ScriptFile = GetOptionalString(element, "scriptFile"),
        Disabled = GetOptionalBool(element, "disabled"),
        EntryPoint = GetOptionalString(element, "entryPoint"),
    };

    private static EquatableArray<BindingModelV1> ParseBindings(JsonElement element)
    {
        ImmutableArray<BindingModelV1>.Builder builder = ImmutableArray.CreateBuilder<BindingModelV1>(element.GetArrayLength());
        foreach (JsonElement binding in element.EnumerateArray())
        {
            builder.Add(ParseBinding(binding));
        }

        return builder.MoveToImmutable();
    }

    private static BindingModelV1 ParseBinding(JsonElement e)
    {
        string name = e.GetProperty("name").GetString()!;
        string type = e.GetProperty("type").GetString()!;
        string directionStr = e.GetProperty("direction").GetString()!;
        BindingDirectionKindV1 direction = directionStr.Equals("in", StringComparison.OrdinalIgnoreCase)
            ? BindingDirectionKindV1.In
            : BindingDirectionKindV1.Out;

        ImmutableArray<BindingExtensionEntryV1>.Builder extensions = ImmutableArray.CreateBuilder<BindingExtensionEntryV1>();
        foreach (JsonProperty prop in e.EnumerateObject())
        {
            if (prop.Name == "name" || prop.Name == "type" || prop.Name == "direction")
            {
                continue;
            }

            BindingExtensionValueV1 value = prop.Value.ValueKind switch
            {
                JsonValueKind.True => new BindingExtensionValueV1(BindingExtensionValueKindV1.Bool, null, true, null),
                JsonValueKind.False => new BindingExtensionValueV1(BindingExtensionValueKindV1.Bool, null, false, null),
                JsonValueKind.Array => new BindingExtensionValueV1(BindingExtensionValueKindV1.StringArray, null, null, GetStringArray(prop.Value)),
                _ => new BindingExtensionValueV1(BindingExtensionValueKindV1.String, prop.Value.GetString(), null, null),
            };

            extensions.Add(new BindingExtensionEntryV1(prop.Name, value));
        }

        return new BindingModelV1
        {
            Name = name,
            Type = type,
            Direction = direction,
            ExtensionData = extensions.ToImmutable(),
        };
    }

    private static MetadataModelV1 ParseMetadata(JsonElement element) => new()
    {
        DefaultFunctionName = element.GetProperty("defaultFunctionName").GetString()!,
        Description = element.GetProperty("description").GetString()!,
        Name = element.GetProperty("name").GetString()!,
        Language = element.GetProperty("language").GetString()!,
        Category = GetStringArray(element.GetProperty("category")),
        CategoryStyle = element.GetProperty("categoryStyle").GetString()!,
        EnabledInTryMode = element.GetProperty("enabledInTryMode").GetBoolean(),
        UserPrompt = GetOptionalStringArray(element, "userPrompt"),
        Filters = GetOptionalStringArray(element, "filters"),
        Trigger = GetOptionalString(element, "trigger"),
    };

    private static string? GetOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement prop) ? prop.GetString() : null;

    private static bool? GetOptionalBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement prop) ? prop.GetBoolean() : null;

    private static EquatableArray<string>? GetOptionalStringArray(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement prop) ? GetStringArray(prop) : null;

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
