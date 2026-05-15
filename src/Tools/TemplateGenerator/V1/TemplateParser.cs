// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1;

internal static class TemplateParser
{
    public static EquatableArray<TemplateModel> Parse(string json, string? language)
    {
        using var doc = JsonDocument.Parse(json);
        ImmutableArray<TemplateModel>.Builder builder = ImmutableArray.CreateBuilder<TemplateModel>(doc.RootElement.GetArrayLength());

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

    private static TemplateModel ParseTemplate(string id, string? suffix, JsonElement element)
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

    private static EquatableArray<FileModel> ParseFiles(JsonElement element)
    {
        ImmutableArray<FileModel>.Builder builder = ImmutableArray.CreateBuilder<FileModel>();
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            builder.Add(new FileModel(prop.Name, prop.Value.GetString()!));
        }

        return builder.ToImmutable();
    }

    private static FunctionModel ParseFunction(JsonElement element) => new()
    {
        Bindings = ParseBindings(element.GetProperty("bindings")),
        ScriptFile = GetOptionalString(element, "scriptFile"),
        Disabled = GetOptionalBool(element, "disabled"),
        EntryPoint = GetOptionalString(element, "entryPoint"),
    };

    private static EquatableArray<BindingModel> ParseBindings(JsonElement element)
    {
        ImmutableArray<BindingModel>.Builder builder = ImmutableArray.CreateBuilder<BindingModel>(element.GetArrayLength());
        foreach (JsonElement binding in element.EnumerateArray())
        {
            builder.Add(ParseBinding(binding));
        }

        return builder.MoveToImmutable();
    }

    private static BindingModel ParseBinding(JsonElement e)
    {
        string name = e.GetProperty("name").GetString()!;
        string type = e.GetProperty("type").GetString()!;
        string directionStr = e.GetProperty("direction").GetString()!;
        BindingDirectionKind direction = directionStr.Equals("in", StringComparison.OrdinalIgnoreCase)
            ? BindingDirectionKind.In
            : BindingDirectionKind.Out;

        ImmutableArray<BindingExtensionEntry>.Builder extensions = ImmutableArray.CreateBuilder<BindingExtensionEntry>();
        foreach (JsonProperty prop in e.EnumerateObject())
        {
            if (prop.Name == "name" || prop.Name == "type" || prop.Name == "direction")
            {
                continue;
            }

            BindingExtensionValue value = prop.Value.ValueKind switch
            {
                JsonValueKind.True => new BindingExtensionValue(BindingExtensionValueKind.Bool, null, true, null),
                JsonValueKind.False => new BindingExtensionValue(BindingExtensionValueKind.Bool, null, false, null),
                JsonValueKind.Array => new BindingExtensionValue(BindingExtensionValueKind.StringArray, null, null, GetStringArray(prop.Value)),
                _ => new BindingExtensionValue(BindingExtensionValueKind.String, prop.Value.GetString(), null, null),
            };

            extensions.Add(new BindingExtensionEntry(prop.Name, value));
        }

        return new BindingModel
        {
            Name = name,
            Type = type,
            Direction = direction,
            ExtensionData = extensions.ToImmutable(),
        };
    }

    private static MetadataModel ParseMetadata(JsonElement element) => new()
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
