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

    [Required]
    public string PackageId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string RuntimeIdentifier { get; set; } = string.Empty;

    public string EntryPointAssemblyPath { get; set; } = string.Empty;

    public string EntryPointType { get; set; } = string.Empty;

    [Required]
    public ITaskItem[] InnerPackages { get; set; } = [];

    public override bool Execute()
    {
        if (!ValidateInputs())
        {
            return false;
        }

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
        if (string.Equals(Kind, "rid-pointer", StringComparison.Ordinal))
        {
            packages = new(StringComparer.Ordinal);
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
            RuntimeIdentifier = string.IsNullOrEmpty(RuntimeIdentifier) ? null : RuntimeIdentifier,
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

    private bool ValidateInputs()
    {
        bool isPointer = string.Equals(Kind, "rid-pointer", StringComparison.Ordinal);
        bool isRidImplementation = !string.IsNullOrEmpty(RuntimeIdentifier);
        bool hasEntryPoint = !string.IsNullOrEmpty(EntryPointAssemblyPath) || !string.IsNullOrEmpty(EntryPointType);

        if (isPointer)
        {
            if (hasEntryPoint)
            {
                Log.LogError("A rid-pointer workload cannot define an entry point.");
            }

            if (isRidImplementation)
            {
                Log.LogError("A rid-pointer workload cannot define a runtime identifier.");
            }

            if (InnerPackages.Length == 0)
            {
                Log.LogError("A rid-pointer workload must define at least one inner package.");
            }
        }
        else if (InnerPackages.Length > 0)
        {
            Log.LogError($"Workload kind '{Kind}' cannot define inner packages.");
        }

        if (isRidImplementation
            && !string.Equals(Kind, "workload", StringComparison.Ordinal)
            && !string.Equals(Kind, "content", StringComparison.Ordinal))
        {
            Log.LogError($"Workload kind '{Kind}' cannot define a runtime identifier.");
        }

        if (isRidImplementation && !IsNormalizedRuntimeIdentifier(RuntimeIdentifier))
        {
            Log.LogError($"Runtime identifier '{RuntimeIdentifier}' must be non-empty and lowercase.");
        }

        if (string.Equals(Kind, "workload", StringComparison.Ordinal))
        {
            if (string.IsNullOrEmpty(EntryPointAssemblyPath) || string.IsNullOrEmpty(EntryPointType))
            {
                Log.LogError("A workload must define both entry-point assembly path and type.");
            }
        }
        else if (hasEntryPoint)
        {
            Log.LogError($"Workload kind '{Kind}' cannot define an entry point.");
        }

        if (isPointer)
        {
            ValidateInnerPackages();
        }

        return !Log.HasLoggedErrors;
    }

    private void ValidateInnerPackages()
    {
        HashSet<string> runtimeIdentifiers = new(StringComparer.OrdinalIgnoreCase);
        foreach (ITaskItem package in InnerPackages)
        {
            string runtimeIdentifier = package.GetMetadata("RuntimeIdentifier");
            if (!IsNormalizedRuntimeIdentifier(runtimeIdentifier))
            {
                Log.LogError($"Inner package '{package.ItemSpec}' has invalid runtime identifier '{runtimeIdentifier}'. Runtime identifiers must be lowercase.");
                continue;
            }

            if (!runtimeIdentifiers.Add(runtimeIdentifier))
            {
                Log.LogError($"Runtime identifier '{runtimeIdentifier}' is defined more than once.");
            }

            string expectedPackageId = $"{PackageId}.{runtimeIdentifier}";
            if (!string.Equals(package.ItemSpec, expectedPackageId, StringComparison.OrdinalIgnoreCase))
            {
                Log.LogError($"Inner package for runtime identifier '{runtimeIdentifier}' must be named '{expectedPackageId}', but was '{package.ItemSpec}'.");
            }
        }
    }

    private static bool IsNormalizedRuntimeIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && string.Equals(value, value.ToLowerInvariant(), StringComparison.Ordinal);

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

    [JsonPropertyName("runtimeIdentifier")]
    public string? RuntimeIdentifier { get; set; }

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
