// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Reads the <c>symbolInfo</c> object, naming each <see cref="FuncTemplateSymbol"/> after its key.
/// </summary>
internal sealed class FuncTemplateSymbolsConverter : JsonConverter<IReadOnlyDictionary<string, FuncTemplateSymbol>>
{
    /// <inheritdoc />
    public override IReadOnlyDictionary<string, FuncTemplateSymbol> Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Message-less exceptions let the serializer report the JSON path.
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        JsonTypeInfo<FuncTemplateSymbol> typeInfo = options.GetTypeInfo<FuncTemplateSymbol>();
        Dictionary<string, FuncTemplateSymbol> symbols = [with(StringComparer.Ordinal)];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            FuncTemplateSymbol symbol = JsonSerializer.Deserialize(ref reader, typeInfo) ?? throw new JsonException();
            symbols[name] = symbol with { Name = name };
        }

        return symbols.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override void Write(
        Utf8JsonWriter writer, IReadOnlyDictionary<string, FuncTemplateSymbol> value, JsonSerializerOptions options)
    {
        JsonTypeInfo<FuncTemplateSymbol> typeInfo = options.GetTypeInfo<FuncTemplateSymbol>();
        writer.WriteStartObject();
        foreach ((string name, FuncTemplateSymbol symbol) in value)
        {
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, symbol, typeInfo);
        }

        writer.WriteEndObject();
    }
}
