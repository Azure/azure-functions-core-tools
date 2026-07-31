// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//
// Vendored and adapted from the .NET Templating engine. The ver2 wire format reproduced here mirrors:
//   repo:   https://github.com/dotnet/templating
//   source: src/Microsoft.TemplateSearch.Common/TemplateSearchCache/*.Json.cs
//           src/Tools/Microsoft.TemplateSearch.TemplateDiscovery/Results/UnifiedPackCheckResultReportWriter.cs
//   version: 10.0.302
// See README.md in this directory for the full provenance note and list of adaptations.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.TemplateDiscovery;

/// <summary>
/// Reads and writes the standard <c>NuGetTemplateSearchInfoVer2.json</c> search-cache format (version "2.0")
/// and its companion <c>nonTemplatePacks.json</c>, so the output stays interchangeable with the upstream
/// dotnet template-search ecosystem.
/// </summary>
internal sealed class SearchCacheStore
{
    internal const string CacheContentDirectory = "SearchCache";
    internal const string SearchMetadataFileName = "NuGetTemplateSearchInfoVer2.json";
    internal const string NonTemplatePacksFileName = "nonTemplatePacks.json";

    private const string FormatVersion = "2.0";

    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Reads the existing search cache (if present), returning each package's JSON keyed by package name.
    /// The raw JSON is preserved so unchanged packages can be carried over verbatim during a <c>--diff</c> run.
    /// </summary>
    public IReadOnlyDictionary<string, JsonObject> ReadExistingIndex(string indexFilePath)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(indexFilePath))
        {
            return result;
        }

        var root = JsonNode.Parse(File.ReadAllText(indexFilePath));
        if (root?["TemplatePackages"] is not JsonArray packages)
        {
            return result;
        }

        foreach (JsonNode? package in packages)
        {
            if (package is JsonObject obj && obj["Name"]?.GetValue<string>() is { Length: > 0 } name)
            {
                result[name] = obj;
            }
        }

        return result;
    }

    /// <summary>
    /// Reads the existing non-template pack skip-list, keyed by package name.
    /// </summary>
    public IReadOnlyDictionary<string, FilteredPackage> ReadNonTemplatePacks(string nonTemplatePacksFilePath)
    {
        var result = new Dictionary<string, FilteredPackage>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(nonTemplatePacksFilePath))
        {
            return result;
        }

        if (JsonNode.Parse(File.ReadAllText(nonTemplatePacksFilePath)) is not JsonArray packages)
        {
            return result;
        }

        foreach (JsonNode? package in packages)
        {
            if (package is JsonObject obj && obj["Name"]?.GetValue<string>() is { Length: > 0 } name)
            {
                string version = obj["Version"]?.GetValue<string>() ?? string.Empty;
                string reason = obj["Reason"]?.GetValue<string>() ?? string.Empty;
                result[name] = new FilteredPackage(name, version, reason);
            }
        }

        return result;
    }

    public void Write(string outputBaseDirectory, IReadOnlyList<JsonObject> packages, IReadOnlyList<FilteredPackage> filteredPackages)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputBaseDirectory);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(filteredPackages);

        string cacheDirectory = Path.Combine(outputBaseDirectory, CacheContentDirectory);
        Directory.CreateDirectory(cacheDirectory);

        WriteIndex(Path.Combine(cacheDirectory, SearchMetadataFileName), packages);
        WriteNonTemplatePacks(Path.Combine(cacheDirectory, NonTemplatePacksFileName), filteredPackages);
    }

    /// <summary>
    /// Builds the ver2 package JSON for a scanned package, applying the same field-omission rules as upstream.
    /// </summary>
    public JsonObject BuildPackageObject(CandidatePackage package, IReadOnlyList<ITemplateInfo> templates)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(templates);

        var obj = new JsonObject { ["Name"] = package.Name };
        if (!string.IsNullOrWhiteSpace(package.Version))
        {
            obj["Version"] = package.Version;
        }

        if (package.TotalDownloads != 0)
        {
            obj["TotalDownloads"] = package.TotalDownloads;
        }

        if (package.Owners.Count == 1)
        {
            obj["Owners"] = package.Owners[0];
        }
        else if (package.Owners.Count > 1)
        {
            obj["Owners"] = new JsonArray([.. package.Owners.Select(o => (JsonNode)o)]);
        }

        if (package.Reserved)
        {
            obj["Reserved"] = true;
        }

        if (!string.IsNullOrWhiteSpace(package.Description))
        {
            obj["Description"] = package.Description;
        }

        if (!string.IsNullOrWhiteSpace(package.IconUrl))
        {
            obj["IconUrl"] = package.IconUrl;
        }

        obj["Templates"] = new JsonArray([.. templates.Select(BuildTemplateObject)]);
        return obj;
    }

    private static JsonObject BuildTemplateObject(ITemplateInfo template)
    {
        var obj = new JsonObject { ["Identity"] = template.Identity };
        if (!string.IsNullOrWhiteSpace(template.GroupIdentity))
        {
            obj["GroupIdentity"] = template.GroupIdentity;
        }

        if (template.Precedence != 0)
        {
            obj["Precedence"] = template.Precedence;
        }

        obj["Name"] = template.Name;
        obj["ShortNameList"] = new JsonArray([.. template.ShortNameList.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => (JsonNode)s)]);

        if (!string.IsNullOrWhiteSpace(template.Author))
        {
            obj["Author"] = template.Author;
        }

        if (!string.IsNullOrWhiteSpace(template.Description))
        {
            obj["Description"] = template.Description;
        }

        if (!string.IsNullOrWhiteSpace(template.ThirdPartyNotices))
        {
            obj["ThirdPartyNotices"] = template.ThirdPartyNotices;
        }

        if (template.Classifications.Count > 0)
        {
            obj["Classifications"] = new JsonArray([.. template.Classifications.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => (JsonNode)c)]);
        }

        if (template.TagsCollection.Count > 0)
        {
            var tags = new JsonObject();
            foreach (KeyValuePair<string, string> tag in template.TagsCollection)
            {
                tags[tag.Key] = tag.Value;
            }

            obj["TagsCollection"] = tags;
        }

        // Parameters, BaselineInfo and PostActions are intentionally omitted: the func search consumer does
        // not read them. See README.md ("Deliberately dropped").
        return obj;
    }

    private static void WriteIndex(string filePath, IReadOnlyList<JsonObject> packages)
    {
        var root = new JsonObject
        {
            ["Version"] = FormatVersion,
            ["TemplatePackages"] = new JsonArray([.. packages]),
        };

        File.WriteAllText(filePath, root.ToJsonString(_writeOptions));
    }

    private static void WriteNonTemplatePacks(string filePath, IReadOnlyList<FilteredPackage> filteredPackages)
    {
        var array = new JsonArray();
        foreach (FilteredPackage package in filteredPackages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            array.Add(new JsonObject
            {
                ["Name"] = package.Name,
                ["Version"] = package.Version,
                ["Reason"] = package.Reason,
                ["TotalDownloads"] = 0,
                ["Owners"] = new JsonArray(),
                ["Reserved"] = false,
            });
        }

        File.WriteAllText(filePath, array.ToJsonString(_writeOptions));
    }
}
