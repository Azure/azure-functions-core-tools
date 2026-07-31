// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Parses <c>func.host.json</c> content into a <see cref="FuncHostFile"/>.
/// Tolerant of missing fields and unknown keys; malformed JSON yields
/// <see cref="FuncHostFile.Empty"/> rather than throwing so one bad host file
/// never fails a catalog scan.
/// </summary>
internal static class FuncHostFileParser
{
    /// <summary>
    /// Parses <paramref name="json"/>, or returns <see cref="FuncHostFile.Empty"/>
    /// when it is null, blank, or not valid JSON.
    /// </summary>
    internal static FuncHostFile Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FuncHostFile.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return FuncHostFile.Empty;
            }

            List<FuncHostSymbolInfo> symbols = [];
            if (root.TryGetProperty("symbolInfo", out JsonElement symbolInfo) && symbolInfo.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement entry in symbolInfo.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string? id = ReadString(entry, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    symbols.Add(new FuncHostSymbolInfo(
                        id,
                        ReadString(entry, "longName"),
                        ReadBool(entry, "isHidden"),
                        ReadValidator(entry)));
                }
            }

            FuncHostValidator? functionNameValidator = null;
            if (root.TryGetProperty("functionName", out JsonElement functionName) && functionName.ValueKind == JsonValueKind.Object)
            {
                functionNameValidator = ReadValidator(functionName);
            }

            return new FuncHostFile(symbols, functionNameValidator);
        }
        catch (JsonException)
        {
            return FuncHostFile.Empty;
        }
    }

    private static FuncHostValidator? ReadValidator(JsonElement parent)
    {
        if (!parent.TryGetProperty("validator", out JsonElement validator) || validator.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? expression = ReadString(validator, "expression");
        return string.IsNullOrWhiteSpace(expression)
            ? null
            : new FuncHostValidator(expression, ReadString(validator, "errorText"));
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string property)
        => element.TryGetProperty(property, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;
}
