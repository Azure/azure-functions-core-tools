// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Workloads.Install;

namespace Azure.Functions.Cli.Tests.Templates;

public sealed partial class BundledNodeTemplateTests
{
    internal string CombinedNodeFixtureArchive()
    {
        string path = Path.Combine(_root, $"combined-node-fixture-{Guid.NewGuid():N}.nupkg");
        File.Copy(BuiltArchive, path);
        using var outer = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = outer.GetEntry(BundledTemplateInstaller.ManifestName)!;
        JsonNode declaration;
        using (StreamReader input = new(entry.Open())) declaration = JsonNode.Parse(input.ReadToEnd())!;
        AddFunctionFixtureAsset(outer, declaration, NodeArtifactVersion);
        entry.Delete();
        using StreamWriter output = new(outer.CreateEntry(BundledTemplateInstaller.ManifestName).Open());
        output.Write(declaration.ToJsonString());
        return path;
    }

    private void AddFunctionFixtureAsset(ZipArchive outer, JsonNode declaration, string version, string? marker = null)
    {
        // Controlled multi-asset owners retain item/cohort process coverage without changing the real basic-only producer.
        using MemoryStream bytes = new();
        using (Stream input = File.OpenRead(FunctionArchive)) input.CopyTo(bytes);
        JsonArray templates = [];
        using (ZipArchive functions = new(bytes, ZipArchiveMode.Update, leaveOpen: true))
        {
            MigrationChangeVersion(functions, version);
            Dictionary<string, byte[]> changed = new(StringComparer.Ordinal);
            if (marker is not null)
            {
                string suffix = "\n// cohort:" + marker + "\n";
                const string itemPath = "content/http-js/httpTrigger.js";
                var item = functions.GetEntry(itemPath)!;
                string body;
                using (StreamReader input = new(item.Open())) body = input.ReadToEnd();
                changed[itemPath] = Encoding.UTF8.GetBytes(body + suffix);
                item.Delete();
                using (Stream output = functions.CreateEntry(itemPath).Open()) output.Write(changed[itemPath]);

                const string originalPath = "provenance/node/v2/templates/templates.json";
                var original = functions.GetEntry(originalPath)!;
                JsonArray corpus;
                using (StreamReader input = new(original.Open())) corpus = JsonNode.Parse(input.ReadToEnd())!.AsArray();
                JsonObject files = corpus.Single(template => template!["id"]!.GetValue<string>() == "HttpTrigger-JavaScript")!["files"]!.AsObject();
                string file = files.Single().Key;
                files[file] = files[file]!.GetValue<string>() + suffix;
                changed[originalPath] = Encoding.UTF8.GetBytes(corpus.ToJsonString());
                original.Delete();
                using (Stream output = functions.CreateEntry(originalPath).Open()) output.Write(changed[originalPath]);
            }
            var provenanceEntry = functions.GetEntry("provenance/node/compatibility.json")!;
            JsonNode provenance;
            using (StreamReader input = new(provenanceEntry.Open())) provenance = JsonNode.Parse(input.ReadToEnd())!;
            provenance["packageVersion"] = version;
            UpdateFixtureSourcePins(provenance, changed).Should().BeGreaterThanOrEqualTo(changed.Count);
            provenanceEntry.Delete();
            using (StreamWriter output = new(functions.CreateEntry("provenance/node/compatibility.json").Open()))
            {
                output.Write(provenance.ToJsonString());
            }
            foreach (var configuration in functions.Entries.Where(entry => entry.FullName.EndsWith("/.template.config/template.json", StringComparison.Ordinal)))
            {
                using Stream input = configuration.Open();
                using var document = JsonDocument.Parse(input);
                templates.Add(new JsonObject
                {
                    ["path"] = configuration.FullName,
                    ["identity"] = document.RootElement.GetProperty("identity").GetString(),
                    ["type"] = "item",
                });
            }
        }
        byte[] archive = bytes.ToArray();
        const string assetPath = "tools/any/templates/node-functions-fixture.nupkg";
        using (Stream output = outer.CreateEntry(assetPath).Open()) output.Write(archive);
        declaration["assets"]!.AsArray().Add(new JsonObject
        {
            ["path"] = assetPath,
            ["packageId"] = "Azure.Functions.Templates.Node.Functions",
            ["version"] = version,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(archive)),
            ["templates"] = templates,
        });
    }

    private static int UpdateFixtureSourcePins(JsonNode node, IReadOnlyDictionary<string, byte[]> changed)
    {
        int count = 0;
        if (node is JsonObject value)
        {
            if (value["path"]?.GetValue<string>() is { } path && changed.TryGetValue(path, out byte[]? content) && value.ContainsKey("sha256"))
            {
                value["sha256"] = Convert.ToHexStringLower(SHA256.HashData(content));
                count++;
            }
            foreach (JsonNode? child in value.Select(pair => pair.Value).Where(child => child is not null))
            {
                count += UpdateFixtureSourcePins(child!, changed);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.Where(child => child is not null)) count += UpdateFixtureSourcePins(child!, changed);
        }
        return count;
    }
}
