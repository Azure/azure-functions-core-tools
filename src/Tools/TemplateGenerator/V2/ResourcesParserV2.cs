// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Text.Json;
using Azure.Functions.Cli.Tools.TemplateGenerator.Model;
using Azure.Functions.Cli.Tools.TemplateGenerator.V2.Model;

namespace Azure.Functions.Cli.Tools.TemplateGenerator.V2;

internal static class ResourcesParserV2
{
    /// <summary>
    /// Parses a single Resources.{culture}.json file. For the default culture ("en") and for files
    /// without a "lang" key (e.g. Resources.en-US.json), entries are read from the "en" root key.
    /// Otherwise entries come from "lang".
    /// </summary>
    public static ResourceCultureModelV2 Parse(string json, string culture)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        bool isDefault = string.Equals(culture, "en", StringComparison.OrdinalIgnoreCase);

        JsonElement entriesEl = default;
        if (!isDefault && root.TryGetProperty("lang", out JsonElement langEl) && langEl.ValueKind == JsonValueKind.Object)
        {
            entriesEl = langEl;
        }
        else if (root.TryGetProperty("en", out JsonElement enEl) && enEl.ValueKind == JsonValueKind.Object)
        {
            entriesEl = enEl;
        }

        ImmutableArray<ResourceEntryModelV2>.Builder builder = ImmutableArray.CreateBuilder<ResourceEntryModelV2>();
        if (entriesEl.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in entriesEl.EnumerateObject())
            {
                builder.Add(new ResourceEntryModelV2(p.Name, p.Value.GetString() ?? string.Empty));
            }
        }

        return new ResourceCultureModelV2
        {
            Culture = culture,
            Entries = builder.ToImmutable(),
        };
    }
}
