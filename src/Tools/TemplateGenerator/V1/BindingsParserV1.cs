// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V1.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V1;

internal static class BindingsParserV1
{
    public static BindingsCatalogModelV1 Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        ImmutableArray<VariableEntryModelV1>.Builder variables = ImmutableArray.CreateBuilder<VariableEntryModelV1>();
        if (root.TryGetProperty("variables", out JsonElement varsEl) && varsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty prop in varsEl.EnumerateObject())
            {
                variables.Add(new VariableEntryModelV1(prop.Name, prop.Value.GetString() ?? string.Empty));
            }
        }

        ImmutableArray<BindingDefinitionModelV1>.Builder bindings = ImmutableArray.CreateBuilder<BindingDefinitionModelV1>();
        if (root.TryGetProperty("bindings", out JsonElement bindingsEl) && bindingsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement b in bindingsEl.EnumerateArray())
            {
                bindings.Add(ParseBinding(b));
            }
        }

        return new BindingsCatalogModelV1
        {
            Variables = variables.ToImmutable(),
            Bindings = bindings.ToImmutable(),
        };
    }

    private static BindingDefinitionModelV1 ParseBinding(JsonElement e) => new()
    {
        Type = e.GetProperty("type").GetString()!,
        DisplayName = GetOptionalString(e, "displayName") ?? string.Empty,
        Direction = ParseDirection(GetOptionalString(e, "direction")),
        EnabledInTryMode = e.TryGetProperty("enabledInTryMode", out JsonElement et) && et.ValueKind == JsonValueKind.True,
        Documentation = GetOptionalString(e, "documentation"),
        Settings = ParseSettings(e),
        Actions = ParseActions(e),
        Extension = ParseExtension(e),
        Rules = ParseRules(e),
    };

    private static BindingDirectionKindV1 ParseDirection(string? value) => value?.ToLowerInvariant() switch
    {
        "in" => BindingDirectionKindV1.In,
        "out" => BindingDirectionKindV1.Out,
        "trigger" => BindingDirectionKindV1.Trigger,
        _ => BindingDirectionKindV1.In,
    };

    private static EquatableArray<BindingSettingModelV1> ParseSettings(JsonElement e)
    {
        if (!e.TryGetProperty("settings", out JsonElement settingsEl) || settingsEl.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<BindingSettingModelV1>.Empty;
        }

        ImmutableArray<BindingSettingModelV1>.Builder builder = ImmutableArray.CreateBuilder<BindingSettingModelV1>(settingsEl.GetArrayLength());
        foreach (JsonElement s in settingsEl.EnumerateArray())
        {
            builder.Add(ParseSetting(s));
        }

        return builder.MoveToImmutable();
    }

    private static BindingSettingModelV1 ParseSetting(JsonElement e)
    {
        return new BindingSettingModelV1
        {
            Name = e.GetProperty("name").GetString()!,
            Value = ParseSettingValueKind(GetOptionalString(e, "value")),
            Required = e.TryGetProperty("required", out JsonElement r) ? r.GetBoolean() : null,
            Label = GetOptionalString(e, "label"),
            Help = GetOptionalString(e, "help"),
            DefaultValue = ParseDefaultValue(e),
            Placeholder = GetOptionalString(e, "placeholder"),
            Resource = GetOptionalString(e, "resource"),
            Enum = ParseEnum(e),
            Validators = ParseValidators(e),
        };
    }

    private static BindingSettingValueKindV1 ParseSettingValueKind(string? value) => value?.ToLowerInvariant() switch
    {
        "string" => BindingSettingValueKindV1.String,
        "boolean" => BindingSettingValueKindV1.Bool,
        "int" => BindingSettingValueKindV1.Int,
        "enum" => BindingSettingValueKindV1.Enum,
        "checkboxlist" => BindingSettingValueKindV1.CheckBoxList,
        _ => BindingSettingValueKindV1.String,
    };

    private static SettingDefaultValueModelV1? ParseDefaultValue(JsonElement e)
    {
        if (!e.TryGetProperty("defaultValue", out JsonElement v))
        {
            return null;
        }

        switch (v.ValueKind)
        {
            case JsonValueKind.String:
                return new SettingDefaultValueModelV1(SettingDefaultValueKindV1.String, v.GetString(), null, null, null);
            case JsonValueKind.True:
                return new SettingDefaultValueModelV1(SettingDefaultValueKindV1.Bool, null, true, null, null);
            case JsonValueKind.False:
                return new SettingDefaultValueModelV1(SettingDefaultValueKindV1.Bool, null, false, null, null);
            case JsonValueKind.Number:
                return new SettingDefaultValueModelV1(SettingDefaultValueKindV1.Long, null, null, v.GetInt64(), null);
            case JsonValueKind.Array:
                return new SettingDefaultValueModelV1(SettingDefaultValueKindV1.StringArray, null, null, null, GetStringArray(v));
            case JsonValueKind.Null:
                return null;
            default:
                return null;
        }
    }

    private static EquatableArray<BindingEnumValueModelV1> ParseEnum(JsonElement e)
    {
        if (!e.TryGetProperty("enum", out JsonElement enumEl) || enumEl.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<BindingEnumValueModelV1>.Empty;
        }

        ImmutableArray<BindingEnumValueModelV1>.Builder builder = ImmutableArray.CreateBuilder<BindingEnumValueModelV1>(enumEl.GetArrayLength());
        foreach (JsonElement v in enumEl.EnumerateArray())
        {
            builder.Add(new BindingEnumValueModelV1(
                v.GetProperty("value").GetString() ?? string.Empty,
                v.GetProperty("display").GetString() ?? string.Empty));
        }

        return builder.MoveToImmutable();
    }

    private static EquatableArray<BindingValidatorModelV1> ParseValidators(JsonElement e)
    {
        if (!e.TryGetProperty("validators", out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<BindingValidatorModelV1>.Empty;
        }

        ImmutableArray<BindingValidatorModelV1>.Builder builder = ImmutableArray.CreateBuilder<BindingValidatorModelV1>(el.GetArrayLength());
        foreach (JsonElement v in el.EnumerateArray())
        {
            builder.Add(new BindingValidatorModelV1(
                GetOptionalString(v, "expression") ?? string.Empty,
                GetOptionalString(v, "errorText") ?? string.Empty));
        }

        return builder.MoveToImmutable();
    }

    private static EquatableArray<BindingActionModelV1> ParseActions(JsonElement e)
    {
        if (!e.TryGetProperty("actions", out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<BindingActionModelV1>.Empty;
        }

        ImmutableArray<BindingActionModelV1>.Builder builder = ImmutableArray.CreateBuilder<BindingActionModelV1>(el.GetArrayLength());
        foreach (JsonElement a in el.EnumerateArray())
        {
            builder.Add(new BindingActionModelV1
            {
                Template = GetOptionalString(a, "template") ?? string.Empty,
                Binding = GetOptionalString(a, "binding") ?? string.Empty,
                Settings = a.TryGetProperty("settings", out JsonElement s) && s.ValueKind == JsonValueKind.Array
                    ? GetStringArray(s)
                    : ImmutableArray<string>.Empty,
            });
        }

        return builder.MoveToImmutable();
    }

    private static BindingExtensionRefModelV1? ParseExtension(JsonElement e)
    {
        if (!e.TryGetProperty("extension", out JsonElement ex) || ex.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new BindingExtensionRefModelV1(
            GetOptionalString(ex, "id") ?? string.Empty,
            GetOptionalString(ex, "version") ?? string.Empty);
    }

    private static EquatableArray<BindingRuleModelV1> ParseRules(JsonElement e)
    {
        if (!e.TryGetProperty("rules", out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<BindingRuleModelV1>.Empty;
        }

        ImmutableArray<BindingRuleModelV1>.Builder builder = ImmutableArray.CreateBuilder<BindingRuleModelV1>(el.GetArrayLength());
        foreach (JsonElement r in el.EnumerateArray())
        {
            builder.Add(new BindingRuleModelV1
            {
                Name = GetOptionalString(r, "name") ?? string.Empty,
                Type = GetOptionalString(r, "type") ?? string.Empty,
                Label = GetOptionalString(r, "label"),
                Help = GetOptionalString(r, "help"),
                Values = ParseRuleValues(r),
            });
        }

        return builder.MoveToImmutable();
    }

    private static EquatableArray<BindingRuleValueModelV1> ParseRuleValues(JsonElement e)
    {
        if (!e.TryGetProperty("values", out JsonElement el) || el.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<BindingRuleValueModelV1>.Empty;
        }

        ImmutableArray<BindingRuleValueModelV1>.Builder builder = ImmutableArray.CreateBuilder<BindingRuleValueModelV1>(el.GetArrayLength());
        foreach (JsonElement v in el.EnumerateArray())
        {
            builder.Add(new BindingRuleValueModelV1
            {
                Value = GetOptionalString(v, "value") ?? string.Empty,
                Display = GetOptionalString(v, "display") ?? string.Empty,
                HiddenSettings = v.TryGetProperty("hiddenSettings", out JsonElement h) && h.ValueKind == JsonValueKind.Array
                    ? GetStringArray(h)
                    : ImmutableArray<string>.Empty,
                ShownSettings = v.TryGetProperty("shownSettings", out JsonElement s) && s.ValueKind == JsonValueKind.Array
                    ? GetStringArray(s)
                    : ImmutableArray<string>.Empty,
            });
        }

        return builder.MoveToImmutable();
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static EquatableArray<string> GetStringArray(JsonElement element)
    {
        ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>(element.GetArrayLength());
        foreach (JsonElement item in element.EnumerateArray())
        {
            builder.Add(item.GetString() ?? string.Empty);
        }

        return builder.MoveToImmutable();
    }
}
