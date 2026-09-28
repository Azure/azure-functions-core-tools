// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Settings;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Reads the <c>func.host.json</c> content the template engine caches for each template.
/// </summary>
internal static class FuncHostTemplateMetadataReader
{
    internal const string FileName = "func.host.json";

    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Reads the func host metadata cached for <paramref name="template"/>.
    /// </summary>
    /// <exception cref="InvalidTemplateMetadataException">The template's func host metadata is malformed.</exception>
    public static FuncHostTemplateMetadata Read(ITemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Read(template.HostConfigPlace, (template as ITemplateInfoHostJsonCache)?.HostData);
    }

    /// <summary>
    /// Parses cached func host metadata, returning <see cref="FuncHostTemplateMetadata.Empty"/> when the template has none.
    /// </summary>
    /// <param name="hostConfigPlace">Location of the template's func host file, or <c>null</c> when it has none.</param>
    /// <param name="hostData">Content the engine cached for that file.</param>
    /// <exception cref="InvalidTemplateMetadataException">The content is missing, isn't a JSON object, or has an invalid value.</exception>
    public static FuncHostTemplateMetadata Read(string? hostConfigPlace, string? hostData)
    {
        if (string.IsNullOrWhiteSpace(hostData))
        {
            // The engine records the file but caches no content when it can't parse it.
            return hostConfigPlace is null
                ? FuncHostTemplateMetadata.Empty
                : throw new InvalidTemplateMetadataException($"{FileName} could not be read. It must contain a JSON object.");
        }

        try
        {
            using var document = JsonDocument.Parse(hostData, _jsonOptions);
            return ReadTemplate(document.RootElement);
        }
        catch (JsonException ex)
        {
            throw new InvalidTemplateMetadataException($"{FileName} is not valid JSON.", ex);
        }
    }

    private static FuncHostTemplateMetadata ReadTemplate(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidTemplateMetadataException($"{FileName} must contain a JSON object.");
        }

        bool isHidden = false;
        Dictionary<string, FuncHostSymbolMetadata> symbols = new(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (IsKey(property, "isHidden"))
            {
                isHidden = ReadBoolean(property, FileName);
            }
            else if (IsKey(property, "symbolInfo"))
            {
                symbols = ReadSymbols(property.Value);
            }
        }

        return new(isHidden, symbols);
    }

    private static Dictionary<string, FuncHostSymbolMetadata> ReadSymbols(JsonElement symbolInfo)
    {
        if (symbolInfo.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidTemplateMetadataException($"{FileName} 'symbolInfo' must be a JSON object.");
        }

        Dictionary<string, FuncHostSymbolMetadata> symbols = new(StringComparer.Ordinal);
        foreach (JsonProperty symbol in symbolInfo.EnumerateObject())
        {
            symbols[symbol.Name] = ReadSymbol(symbol);
        }

        return symbols;
    }

    private static FuncHostSymbolMetadata ReadSymbol(JsonProperty symbol)
    {
        string source = $"{FileName} symbol '{symbol.Name}'";
        if (symbol.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidTemplateMetadataException($"{source} must be a JSON object.");
        }

        string? longName = null;
        string? shortName = null;
        bool isHidden = false;
        bool alwaysShow = false;
        foreach (JsonProperty property in symbol.Value.EnumerateObject())
        {
            if (IsKey(property, "longName"))
            {
                longName = ReadOptionalString(property, source) ?? throw InvalidValue(property, source);
            }
            else if (IsKey(property, "shortName"))
            {
                shortName = ReadOptionalString(property, source);
            }
            else if (IsKey(property, "isHidden"))
            {
                isHidden = ReadBoolean(property, source);
            }
            else if (IsKey(property, "alwaysShow"))
            {
                alwaysShow = ReadBoolean(property, source);
            }
        }

        return new(longName, shortName, isHidden, alwaysShow);
    }

    private static string? ReadOptionalString(JsonProperty property, string source) => property.Value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => property.Value.GetString() is { Length: > 0 } value ? value : null,
        _ => throw InvalidValue(property, source),
    };

    private static bool ReadBoolean(JsonProperty property, string source) => property.Value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String when bool.TryParse(property.Value.GetString(), out bool value) => value,
        _ => throw InvalidValue(property, source),
    };

    private static bool IsKey(JsonProperty property, string key) => string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase);

    private static InvalidTemplateMetadataException InvalidValue(JsonProperty property, string source)
        => new($"{source} has an invalid '{property.Name}' value: {property.Value.GetRawText()}");
}
