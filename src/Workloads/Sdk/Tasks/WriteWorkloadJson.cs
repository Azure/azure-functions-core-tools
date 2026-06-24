// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Build.Framework;

namespace Azure.Functions.Cli.Workloads.Sdk.Tasks;

public sealed class WriteWorkloadJson : Microsoft.Build.Utilities.Task
{
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string Schema { get; set; } = string.Empty;

    [Required]
    public string Kind { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string EntryPointAssemblyPath { get; set; } = string.Empty;

    public string EntryPointType { get; set; } = string.Empty;

    [Required]
    public ITaskItem[] InnerPackages { get; set; } = [];

    public override bool Execute()
    {
        EntryPointModel? entryPoint = null;
        if (!string.IsNullOrEmpty(EntryPointAssemblyPath) && !string.IsNullOrEmpty(EntryPointType))
        {
            entryPoint = new EntryPointModel
            {
                AssemblyPath = EntryPointAssemblyPath,
                Type = EntryPointType,
            };
        }

        Dictionary<string, string>? packages = null;
        if (InnerPackages is { Length: > 0 })
        {
            packages = new(StringComparer.OrdinalIgnoreCase);
            foreach (ITaskItem package in InnerPackages)
            {
                packages[package.GetMetadata("RuntimeIdentifier")] = package.ItemSpec;
            }
        }

        WorkloadJsonModel model = new()
        {
            Schema = Schema,
            Kind = Kind,
            DisplayName = string.IsNullOrEmpty(DisplayName) ? null : DisplayName,
            Description = string.IsNullOrEmpty(Description) ? null : Description,
            EntryPoint = entryPoint,
            Packages = packages,
        };

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(
            model, WorkloadJsonContext.Default.WorkloadJsonModel);

        string dir = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Write only when different to preserve timestamps for incremental builds.
        if (File.Exists(OutputPath) && FileContentEquals(OutputPath, content))
        {
            return true;
        }

        File.WriteAllBytes(OutputPath, content);
        return true;
    }

    private static bool FileContentEquals(string path, byte[] expected)
    {
        byte[] existing = File.ReadAllBytes(path);
        if (existing.Length != expected.Length)
        {
            return false;
        }

        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != expected[i])
            {
                return false;
            }
        }

        return true;
    }
}

[JsonSerializable(typeof(WorkloadJsonModel))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class WorkloadJsonContext : JsonSerializerContext;

internal sealed class WorkloadJsonModel
{
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("entryPoint")]
    public EntryPointModel? EntryPoint { get; set; }

    [JsonPropertyName("packages")]
    public IDictionary<string, string>? Packages { get; set; }
}

internal sealed class EntryPointModel
{
    [JsonPropertyName("assemblyPath")]
    public string AssemblyPath { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}
