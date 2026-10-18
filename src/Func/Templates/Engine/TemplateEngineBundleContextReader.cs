// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Captures binding metadata only from the selected resolution's local bundle payload.
/// </summary>
internal sealed class TemplateEngineBundleContextReader(IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public async Task<TemplateEngineBundleContext> ReadAsync(ExtensionBundleResolution.Resolved resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolution.Path);
        cancellationToken.ThrowIfCancellationRequested();
        TemplateEngineBundleContext source = new(resolution.BundleId, resolution.Version, resolution.Path);
        string metadataPath = Path.Combine(source.Path!, source.Version, "bin", "extensions.json");
        string? metadata;
        try
        {
            metadata = await _fileSystem.ReadAllTextIfExistsAsync(metadataPath, cancellationToken);
        }
        catch (IOException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(source, metadataPath, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(source, metadataPath, exception.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (metadata is null)
        {
            return Failed(source, metadataPath, "The metadata file is missing.");
        }

        IReadOnlyList<string> bindings;
        try
        {
            bindings = ReadBindings(metadata);
        }
        catch (JsonException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(source, metadataPath, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(source, metadataPath, exception.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(source.Id, source.Version, source.Path, new(source, bindings));
    }

    private static TemplateEngineBundleContext Failed(TemplateEngineBundleContext source, string metadataPath, string reason)
        => new(source.Id, source.Version, source.Path, new(source, [],
            $"Cannot read binding capabilities for bundle '{source.Id}' {source.Version} from '{metadataPath}'. {reason} Repair the selected bundle and start a new template flow."));

    private static IReadOnlyList<string> ReadBindings(string metadata)
    {
        using var document = JsonDocument.Parse(metadata);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || FindProperty(root, "extensions") is not { ValueKind: JsonValueKind.Array } extensions)
        {
            throw new InvalidDataException("Expected an extensions array.");
        }

        List<string> names = [];
        foreach (JsonElement extension in extensions.EnumerateArray())
        {
            if (extension.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Each extension must be an object.");
            }

            JsonElement? bindings = FindProperty(extension, "bindings");
            if (bindings is null)
            {
                continue;
            }

            if (bindings.Value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Extension bindings must be an array.");
            }

            foreach (JsonElement binding in bindings.Value.EnumerateArray())
            {
                if (binding.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("Each binding name must be a non-empty string.");
                }

                string? name;
                try
                {
                    name = binding.GetString();
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidDataException("A binding name contains invalid Unicode.", exception);
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidDataException("Each binding name must be a non-empty string.");
                }

                names.Add(name);
            }
        }

        return names.AsReadOnly();
    }

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        JsonElement? result = null;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string propertyName;
            try
            {
                propertyName = property.Name;
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException("The metadata contains an invalid Unicode property name.", exception);
            }

            if (!string.Equals(propertyName, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (result is not null)
            {
                throw new InvalidDataException($"The metadata repeats '{name}'.");
            }

            result = property.Value;
        }

        return result;
    }
}
