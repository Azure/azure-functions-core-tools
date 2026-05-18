// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Azure.Functions.Cli.Projects;

/// <summary>
/// File-write and <c>host.json</c>-merge helpers used by project initializers.
/// </summary>
public static class ProjectFiles
{
    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Writes <paramref name="contents"/> to <paramref name="absolutePath"/> when missing or when <paramref name="force"/> is true.
    /// </summary>
    public static bool WriteIfMissing(string absolutePath, string contents, bool force)
    {
        if (File.Exists(absolutePath) && !force)
        {
            return false;
        }

        string? dir = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(absolutePath, contents);
        return true;
    }

    /// <summary>
    /// Reads an embedded template by relative path from <paramref name="assembly"/>.
    /// Throws when the resource is missing so build-time mistakes surface loudly.
    /// Templates must live under a <c>Templates/</c> folder and be marked as
    /// <c>EmbeddedResource</c> in the workload csproj.
    /// </summary>
    public static string ReadTemplate(Assembly assembly, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        string resourceName = $"{assembly.GetName().Name}.Templates.{relativePath.Replace('/', '.')}";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded template '{resourceName}' is missing from {assembly.GetName().Name}.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Loads <paramref name="hostJsonPath"/>, applies <paramref name="mutate"/>, and writes it back.
    /// Falls back to a fresh object when missing or malformed so merges always succeed.
    /// </summary>
    public static void MergeHostJson(string hostJsonPath, Action<JsonObject> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        JsonObject root = LoadHostJsonObject(hostJsonPath);
        mutate(root);
        File.WriteAllText(hostJsonPath, root.ToJsonString(_writeOptions) + Environment.NewLine);
    }

    private static JsonObject LoadHostJsonObject(string hostJsonPath)
    {
        if (!File.Exists(hostJsonPath))
        {
            return new JsonObject { ["version"] = "2.0" };
        }

        try
        {
            string raw = File.ReadAllText(hostJsonPath);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new JsonObject { ["version"] = "2.0" };
            }

            var parsed = JsonNode.Parse(raw);
            if (parsed is JsonObject obj)
            {
                return obj;
            }
        }
        catch (JsonException)
        {
            // Treat malformed user host.json as empty rather than fail init.
        }

        return new JsonObject { ["version"] = "2.0" };
    }
}
