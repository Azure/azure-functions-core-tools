// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Captures the bounded direct-source CommonJS Node programming-model-v4 configuration capability.
/// </summary>
internal sealed class NodeProjectCapabilityReader(NodeProjectFileReader files)
{
    internal const string CapabilityId = "azure.functions.node.model-v4.javascript-commonjs";

    private static readonly string[] _paths =
    [
        "package.json", "host.json", "tsconfig.json", ".func/config.json",
        "local.settings.json", "src/package.json", "src/functions/package.json",
    ];

    private readonly NodeProjectFileReader _files = files ?? throw new ArgumentNullException(nameof(files));

    public async Task<IReadOnlyList<ProjectCapability>> ReadAsync(string projectRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (!Path.IsPathFullyQualified(projectRoot) || projectRoot.Contains('\0'))
        {
            throw new ArgumentException("Capability inspection requires a fully qualified project root.", nameof(projectRoot));
        }

        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, (NodeProjectFileKind Kind, byte[] Bytes)> observations = new(StringComparer.Ordinal);
        foreach (string path in _paths)
        {
            try
            {
                (NodeProjectFileKind kind, byte[] bytes) = await _files.ReadAsync(projectRoot, path, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                observations.Add(path, (kind, [.. bytes]));
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new UnauthorizedAccessException($"Cannot read Node project evidence '{path}'.", exception);
            }
            catch (IOException exception)
            {
                throw new IOException($"Cannot capture Node project evidence '{path}'.", exception);
            }
        }

        ProjectCapabilitySupport? support = Interpret(observations);
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly<ProjectCapability>(support is { } reported ? [new(CapabilityId, reported)] : []);
    }

    private static ProjectCapabilitySupport? Interpret(IReadOnlyDictionary<string, (NodeProjectFileKind Kind, byte[] Bytes)> observations)
    {
        using JsonDocument? package = Parse(observations, "package.json");
        using JsonDocument? host = Parse(observations, "host.json", configuration: true);
        using JsonDocument? config = Parse(observations, ".func/config.json", configuration: true);
        using JsonDocument? settings = Parse(observations, "local.settings.json", configuration: true);

        JsonElement? packageRoot = package?.RootElement;
        JsonElement? dependencies = Property(packageRoot, "dependencies", JsonValueKind.Object, "package.json");
        string? sdk = ReadString(dependencies, "@azure/functions", "package.json");
        string? main = ReadString(packageRoot, "main", "package.json");
        string? mode = ReadString(packageRoot, "type", "package.json");
        bool alternateSdk = Property(packageRoot, "overrides", null, "package.json") is not null;
        string[] alternativeSections = ["devDependencies", "optionalDependencies", "peerDependencies"];
        foreach (string section in alternativeSections)
        {
            JsonElement? alternatives = Property(packageRoot, section, JsonValueKind.Object, "package.json");
            alternateSdk |= Property(alternatives, "@azure/functions", null, "package.json") is not null;
        }

        string? hostVersion = ReadString(host?.RootElement, "version", "host.json");
        JsonElement? stack = Property(config?.RootElement, "stack", JsonValueKind.Object, ".func/config.json", StringComparison.OrdinalIgnoreCase);
        (string? runtime, bool canonicalRuntime) = ReadCliStackProperty(config?.RootElement, stack, "runtime");
        (string? language, bool canonicalLanguage) = ReadCliStackProperty(config?.RootElement, stack, "language");
        JsonElement? values = Property(settings?.RootElement, "Values", JsonValueKind.Object, "local.settings.json");
        string? worker = ReadString(values, "FUNCTIONS_WORKER_RUNTIME", "local.settings.json", StringComparison.OrdinalIgnoreCase);
        JsonElement? encrypted = Property(settings?.RootElement, "IsEncrypted", null, "local.settings.json");
        if (encrypted is { ValueKind: not JsonValueKind.True and not JsonValueKind.False })
        {
            throw new FormatException("Node project evidence 'local.settings.json' property 'IsEncrypted' must be a boolean.");
        }

        if (observations.Values.Any(observation => observation.Kind is not NodeProjectFileKind.Absent and not NodeProjectFileKind.RegularFile)
            || package is null || host is null
            || sdk != "^4.0.0" || main != "src/functions/*.js" || hostVersion != "2.0"
            || alternateSdk || encrypted is { ValueKind: JsonValueKind.True }
            || observations["tsconfig.json"].Kind != NodeProjectFileKind.Absent
            || observations["src/package.json"].Kind != NodeProjectFileKind.Absent
            || observations["src/functions/package.json"].Kind != NodeProjectFileKind.Absent
            || !canonicalRuntime || !canonicalLanguage
            || !MatchesOrAbsent(runtime, "node") || !MatchesOrAbsent(language, "JavaScript") || !MatchesOrAbsent(worker, "node"))
        {
            return null;
        }

        return mode switch
        {
            null or "commonjs" => ProjectCapabilitySupport.Supported,
            "module" => ProjectCapabilitySupport.Unsupported,
            _ => null,
        };
    }

    private static (string? Value, bool IsCanonical) ReadCliStackProperty(JsonElement? root, JsonElement? stack, string name)
    {
        const string Path = ".func/config.json";
        string effectiveKey = $"stack:{name}";
        string? nested = ReadString(stack, name, Path, StringComparison.OrdinalIgnoreCase);
        string? flat = ReadString(root, effectiveKey, Path, StringComparison.OrdinalIgnoreCase);
        if (nested is not null && flat is not null)
        {
            throw new FormatException($"Node project evidence '{Path}' repeats effective property '{effectiveKey}'.");
        }

        bool descendant = root?.EnumerateObject().Any(property =>
                property.Name.StartsWith(effectiveKey + ":", StringComparison.OrdinalIgnoreCase)) == true
            || stack?.EnumerateObject().Any(property =>
                property.Name.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) == true;
        return (nested, flat is null && !descendant);
    }

    private static JsonDocument? Parse(
        IReadOnlyDictionary<string, (NodeProjectFileKind Kind, byte[] Bytes)> observations, string path, bool configuration = false)
    {
        (NodeProjectFileKind kind, byte[] bytes) = observations[path];
        if (kind != NodeProjectFileKind.RegularFile)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = configuration,
                CommentHandling = configuration ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
            });
        }
        catch (JsonException exception)
        {
            throw new FormatException($"Node project evidence '{path}' is not valid JSON.", exception);
        }

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            document.Dispose();
            throw new FormatException($"Node project evidence '{path}' must contain a JSON object.");
        }

        return document;
    }

    private static JsonElement? Property(
        JsonElement? parent, string name, JsonValueKind? kind, string path, StringComparison comparison = StringComparison.Ordinal)
    {
        if (parent is null)
        {
            return null;
        }

        JsonElement? found = null;
        foreach (JsonProperty property in parent.Value.EnumerateObject())
        {
            string propertyName;
            try
            {
                propertyName = property.Name;
            }
            catch (InvalidOperationException exception)
            {
                throw new FormatException($"Node project evidence '{path}' has an invalid Unicode property name.", exception);
            }

            if (!string.Equals(propertyName, name, comparison))
            {
                continue;
            }

            if (found is not null)
            {
                throw new FormatException($"Node project evidence '{path}' repeats interpreted property '{name}'.");
            }

            if (kind is not null && property.Value.ValueKind != kind)
            {
                throw new FormatException($"Node project evidence '{path}' property '{name}' must be a JSON {kind}.");
            }

            found = property.Value;
        }

        return found;
    }

    private static string? ReadString(
        JsonElement? parent, string name, string path, StringComparison comparison = StringComparison.Ordinal)
    {
        JsonElement? value = Property(parent, name, JsonValueKind.String, path, comparison);
        try
        {
            return value?.GetString();
        }
        catch (InvalidOperationException exception)
        {
            throw new FormatException($"Node project evidence '{path}' property '{name}' has an invalid Unicode string.", exception);
        }
    }

    private static bool MatchesOrAbsent(string? value, string expected)
        => value is null || string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
}
