// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates.Engine;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Projects;

internal sealed record OwnedProjectPreparationDefinition(
    string Stack, string Language, string PackageId, string Identity, string ManifestPath, IReadOnlyList<string> Sources);

internal sealed record QualifiedProjectPreparationSource(
    string ManifestSha256, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Sources);

/// <summary>
/// Checks the exact bundled stack project sources before an owned workflow adapter can use them.
/// </summary>
internal sealed class OwnedProjectPreparationSource(IFileSystem fileSystem)
{
    private const string ConfigurationPath = "content/project/.template.config/template.json";
    private readonly IFileSystem _files = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public static bool Handles(TemplateCatalogEntry entry, OwnedProjectPreparationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(definition);
        return entry.Type == TemplateType.Project && entry.Identity == definition.Identity && entry.Language == definition.Language
            && entry.Package is { Version: not null } package && package.Identifier == definition.PackageId;
    }

    public async Task<QualifiedProjectPreparationSource> QualifyAsync(NativeProjectPreparationContext context,
        OwnedProjectPreparationDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        ResolvedTemplate selected = context.SelectedTemplate;
        if (!Handles(selected.Entry, definition) || context.NativePreview.Status != TemplateInvocationStatus.Success
            || context.NativePreview.ProjectConfigurations.Count != 1)
        {
            throw Invalid("Owned preparation requires one selected bundled project and its validated native preview.");
        }
        TemplateProjectConfigurationPlan project = context.NativePreview.ProjectConfigurations[0];
        if (project.Stack != definition.Stack || project.Language != definition.Language
            || Path.GetFullPath(project.Root) != context.TargetDirectory.Info.FullName)
        {
            throw Invalid("Owned preparation target, stack or language does not match the selected native project.");
        }
        return await QualifyAsync(selected, definition, cancellationToken);
    }

    public async Task<QualifiedProjectPreparationSource> QualifyAsync(ResolvedTemplate selected,
        OwnedProjectPreparationDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Handles(selected.Entry, definition)) throw Invalid("Owned preparation requires its exact selected native package and identity.");
        TemplateInvocationResult? changed = await selected.ValidateConfigurationAsync(cancellationToken);
        if (changed is not null) throw Invalid(changed.Diagnostic ?? "The selected project configuration changed.");
        ReadOnlyMemory<byte> bytes = await selected.ReadPackageFileAsync(definition.ManifestPath, cancellationToken);
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new StreamReader(stream);
        using var manifest = JsonDocument.Parse(await reader.ReadToEndAsync(cancellationToken));
        JsonElement root = manifest.RootElement;
        JsonElement schema = Property(root, "schemaVersion");
        if (schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int schemaVersion) || schemaVersion != 1
            || Text(root, "packageId") != definition.PackageId
            || Text(root, "identity") != definition.Identity || Text(root, "stack") != definition.Stack
            || Text(root, "language") != definition.Language
            || !NuGetVersion.TryParse(Text(root, "packageVersion"), out NuGetVersion? authoredVersion)
            || !NuGetVersion.TryParse(selected.Entry.Package!.Version, out NuGetVersion? installedVersion)
            || authoredVersion != installedVersion)
        {
            throw Invalid("Owned project provenance does not match the selected package ID/version, identity, stack or language.");
        }
        JsonElement sourceArray = Property(root, "sources");
        if (sourceArray.ValueKind != JsonValueKind.Array) throw Invalid("Owned project provenance sources must be an array.");
        Dictionary<string, ReadOnlyMemory<byte>> sources = new(StringComparer.Ordinal);
        foreach (JsonElement source in sourceArray.EnumerateArray())
        {
            string path = Text(source, "path");
            string hash = Text(source, "sha256");
            if (!definition.Sources.Contains(path, StringComparer.Ordinal) || sources.ContainsKey(path)
                || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            {
                throw Invalid("Owned project provenance contains an unsupported, duplicated or malformed source descriptor.");
            }
            ReadOnlyMemory<byte> content = await selected.ReadPackageFileAsync(path, cancellationToken);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(content.Span)), hash, StringComparison.OrdinalIgnoreCase)
                || (path == ConfigurationPath && !string.Equals(hash, selected.ConfigurationSha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw Invalid($"Owned project source '{path}' changed (SHA-256 mismatch).");
            }
            sources.Add(path, content);
        }
        if (sources.Count != definition.Sources.Count)
        {
            throw Invalid("Owned project provenance does not contain the complete expected source inventory.");
        }
        using var configuration = JsonDocument.Parse(sources[ConfigurationPath]);
        if (Text(configuration.RootElement, "identity") != definition.Identity)
        {
            throw Invalid("Owned native configuration names a different project identity.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes.Span)), sources);
    }

    public async Task<string?> ValidateCreatedAsync(NativeProjectPreparationContext context,
        OwnedProjectPreparationDefinition definition, IReadOnlyList<string> requiredFiles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(requiredFiles);
        cancellationToken.ThrowIfCancellationRequested();
        string root = context.TargetDirectory.Info.FullName;
        if (requiredFiles.Any(path => !_files.FileExists(Path.Combine(root, path))))
        {
            return "Native project files must exist before owned stack preparation.";
        }
        string configurationPath = context.NativePreview.ProjectConfigurations[0].ConfigurationPath;
        string? text = await _files.ReadAllTextIfExistsAsync(configurationPath, cancellationToken);
        if (text is null) return "Mandatory CLI configuration must exist before owned stack preparation.";
        using var configuration = JsonDocument.Parse(text);
        JsonElement stack = Property(configuration.RootElement, "stack");
        return Text(stack, "runtime") == definition.Stack && Text(stack, "language") == definition.Language
            ? null : "Mandatory CLI configuration does not match the selected project's canonical stack and language.";
    }

    internal static JsonElement Property(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement property)
            ? property : throw Invalid($"Owned project provenance or configuration is missing '{name}'.");

    internal static string Text(JsonElement value, string name)
        => Property(value, name) is { ValueKind: JsonValueKind.String } property && property.GetString() is { Length: > 0 } text
            ? text : throw Invalid($"Owned project provenance or configuration '{name}' must be a nonempty string.");

    private static InvalidDataException Invalid(string message) => new(message);
}
