// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;

namespace Azure.Functions.Cli.Templates.Search;

/// <summary>
/// Parses the <c>NuGetTemplateSearchInfoVer2.json</c> search-cache format into
/// the CLI's <see cref="FuncSearchIndex"/> model. The property names and the
/// string-or-array <c>Owners</c> shape mirror the discovery tool's writer so
/// the format stays interchangeable with the wider templating ecosystem.
/// </summary>
internal static class FuncSearchIndexReader
{
    private const string SupportedVersion = "2.0";

    /// <summary>
    /// Parses the ver2 search-cache JSON.
    /// </summary>
    /// <exception cref="FuncSearchIndexFormatException">
    /// The JSON is malformed, carries an unsupported schema version, or is
    /// missing the required <c>TemplatePackages</c> array.
    /// </exception>
    public static FuncSearchIndex Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FuncSearchIndexFormatException("The template search index is not valid JSON.", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FuncSearchIndexFormatException("The template search index root must be a JSON object.");
            }

            string version = root.TryGetProperty("Version", out JsonElement versionElement) && versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()!
                : throw new FuncSearchIndexFormatException("The template search index is missing a 'Version' property.");

            if (!string.Equals(version, SupportedVersion, StringComparison.Ordinal))
            {
                throw new FuncSearchIndexFormatException(
                    $"Unsupported template search index version '{version}'. Expected '{SupportedVersion}'.");
            }

            if (!root.TryGetProperty("TemplatePackages", out JsonElement packagesElement) || packagesElement.ValueKind != JsonValueKind.Array)
            {
                throw new FuncSearchIndexFormatException("The template search index is missing the 'TemplatePackages' array.");
            }

            List<FuncSearchPackage> packages = [];
            foreach (JsonElement packageElement in packagesElement.EnumerateArray())
            {
                FuncSearchPackage? package = TryReadPackage(packageElement);
                if (package is not null)
                {
                    packages.Add(package);
                }
            }

            return new FuncSearchIndex(version, packages);
        }
    }

    private static FuncSearchPackage? TryReadPackage(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? name = ReadString(element, "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string? version = ReadString(element, "Version");
        string? description = ReadString(element, "Description");
        IReadOnlyList<string> owners = ReadStringOrArray(element, "Owners");

        List<FuncSearchTemplate> templates = [];
        if (element.TryGetProperty("Templates", out JsonElement templatesElement) && templatesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement templateElement in templatesElement.EnumerateArray())
            {
                FuncSearchTemplate? template = TryReadTemplate(templateElement);
                if (template is not null)
                {
                    templates.Add(template);
                }
            }
        }

        return new FuncSearchPackage(name!, version, owners, description, templates);
    }

    private static FuncSearchTemplate? TryReadTemplate(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? identity = ReadString(element, "Identity");
        if (string.IsNullOrWhiteSpace(identity))
        {
            return null;
        }

        string name = ReadString(element, "Name") ?? identity!;
        IReadOnlyList<string> shortNames = ReadStringArray(element, "ShortNameList");
        string? author = ReadString(element, "Author");
        string? description = ReadString(element, "Description");
        IReadOnlyList<string> classifications = ReadStringArray(element, "Classifications");
        IReadOnlyDictionary<string, string> tags = ReadStringMap(element, "TagsCollection");

        return new FuncSearchTemplate(identity!, name, shortNames, author, description, classifications, tags);
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<string> items = [];
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
            {
                items.Add(text);
            }
        }

        return items;
    }

    private static IReadOnlyList<string> ReadStringOrArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() is { Length: > 0 } single ? [single] : [];
        }

        return value.ValueKind == JsonValueKind.Array ? ReadStringArray(element, propertyName) : [];
    }

    private static IReadOnlyDictionary<string, string> ReadStringMap(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } text)
            {
                map[property.Name] = text;
            }
        }

        return map;
    }
}
