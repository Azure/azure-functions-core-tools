// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Azure.Functions.Cli.Workloads.Go;

/// <summary>
/// File-write and <c>host.json</c>-merge helpers for the Go workload.
/// </summary>
internal static class ProjectFiles
{
    private static readonly Assembly _assembly = typeof(ProjectFiles).Assembly;

    private static readonly JsonSerializerOptions _writeOptions = new()
    {
        WriteIndented = true,
    };

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

    public static string ReadTemplate(string relativePath)
    {
        string resourceName = $"{_assembly.GetName().Name}.Templates.{relativePath.Replace('/', '.')}";
        using Stream? stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"Embedded template '{resourceName}' is missing from {_assembly.GetName().Name}.");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    public static void MergeHostJson(string hostJsonPath, Action<JsonObject> mutate)
    {
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
