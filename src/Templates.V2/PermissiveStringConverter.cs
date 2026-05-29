// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.Functions.Cli.Templates.V2;

/// <summary>
/// JSON converter for v2 template / user-prompt fields whose upstream value
/// is intended as a string but historically appears as a boolean, number, or
/// null in real workload payloads (e.g. <c>userPrompts.json</c> entries with
/// <c>"defaultValue": true</c>). Reads any JSON scalar and reproduces a
/// string representation; non-scalar tokens become <c>null</c>.
/// </summary>
internal sealed class PermissiveStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Number => reader.GetRawValue(),
            JsonTokenType.Null => null,
            _ => SkipAndReturnNull(ref reader),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }

    private static string? SkipAndReturnNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}

internal static class Utf8JsonReaderExtensions
{
    public static string GetRawValue(this Utf8JsonReader reader)
    {
        // Numbers are within ValueSpan in practice. The multi-segment
        // ValueSequence path only applies to very large strings, which a
        // template prompt default won't hit.
        return System.Text.Encoding.UTF8.GetString(reader.ValueSpan);
    }
}
