// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text.Json;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.V2;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Node;

/// <summary>
/// Qualifies original Node item defaults and validators from the selected native package for the unused command bridge.
/// </summary>
internal sealed class NodeItemCompatibility(IInteractionService interaction, Func<string?>? updateHint = null) : INativeItemCompatibility
{
    internal const string ManifestPath = "provenance/node/compatibility.json";
    internal const string PackageId = "Azure.Functions.Templates.Node.Basic";
    private const string TemplatesPath = "provenance/node/v2/templates/templates.json";
    private const string PromptsPath = "provenance/node/v2/bindings/userPrompts.json";
    private const string ResourcesPath = "provenance/node/v2/resources/Resources.json";
    private const string BindingsPath = "provenance/node/v2/templates/_bindings.json";

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    internal string DescribeUnsupportedParameters(NodeItemParameterCompatibilityException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Message + " "
            + (updateHint?.Invoke() ?? "Explicitly update the installed Node workload that owns this selected template archive, then retry. "
                + "Use 'func workload list' to identify that owner and 'func workload update <owner-package-id> --exact' to update it.")
            + " Nothing was generated; no workload was upgraded automatically.";
    }

    public async Task<NativeItemCompatibilityResult> ResolveAsync(ResolvedTemplate selected, TemplateCandidateParseResult arguments,
        string? requestedName, bool canPrompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(arguments);
        if (selected.Entry.Type != TemplateType.Item || selected.Entry.Identity != arguments.TemplateIdentity)
        {
            throw new ArgumentException("Node compatibility requires the selected item and its own parsed arguments.", nameof(arguments));
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            NodeItemSource source = await ReadSourceAsync(selected, cancellationToken);
            NodeItemSourceQualifier.ValidateArguments(source, arguments.ValuesByCanonicalName);
            NativeTemplateLegacyContract contract = source.Contract;
            NativeItemCompatibility compatibility = new(_interaction, _ => contract, async (candidate, path, token) =>
            {
                // Re-read all source bindings before handing original bytes to the shared validator.
                NodeItemSource current = await ReadSourceAsync(candidate, token);
                NodeItemSourceQualifier.ValidateArguments(current, arguments.ValuesByCanonicalName);
                return await candidate.ReadPackageFileAsync(path, token);
            });
            return await compatibility.ResolveAsync(selected, arguments, requestedName, canPrompt, cancellationToken);
        }
        catch (NodeItemParameterCompatibilityException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(null, "unresolved", [DescribeUnsupportedParameters(exception)]);
        }
        catch (InvalidDataException exception)
        {
            return Failed(exception, cancellationToken);
        }
        catch (JsonException exception)
        {
            return Failed(exception, cancellationToken);
        }
        catch (IOException exception)
        {
            return Failed(exception, cancellationToken);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failed(exception, cancellationToken);
        }
    }

    internal static async Task<NodeItemSource> ReadSourceAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        TemplateCatalogEntry entry = selected.Entry;
        if (entry.Package is not { } package || package.Identifier != PackageId || package.Version is null)
        {
            throw Invalid("The selected item is not supplied by the packaged Node native archive.");
        }

        TemplateInvocationResult? changed = await selected.ValidateConfigurationAsync(cancellationToken);
        if (changed is not null)
        {
            throw Invalid(changed.Diagnostic ?? "The selected Node configuration changed.");
        }

        ReadOnlyMemory<byte> manifestBytes = await selected.ReadPackageFileAsync(ManifestPath, cancellationToken);
        List<TemplateEffectSource> pins = [new(entry.Identity, ManifestPath, Convert.ToHexString(SHA256.HashData(manifestBytes.Span)))];
        using var manifest = JsonDocument.Parse(manifestBytes);
        JsonElement root = manifest.RootElement;
        if (Text(root, "packageId") != package.Identifier
            || !NuGetVersion.TryParse(Text(root, "packageVersion"), out NuGetVersion? version)
            || !NuGetVersion.TryParse(package.Version, out NuGetVersion? installedVersion) || version != installedVersion)
        {
            throw Invalid("The Node source provenance does not match the selected native package ID/version.");
        }

        JsonElement[] mappings = [.. Array(root, "templates").Where(mapping => Text(mapping, "identity") == entry.Identity)];
        if (mappings.Length != 1)
        {
            throw Invalid("The selected native identity has no unique original Node source mapping.");
        }

        JsonElement mapping = mappings[0];
        JsonElement configuration = Property(mapping, "configuration");
        if (!string.Equals(Text(configuration, "sha256"), selected.ConfigurationSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The Node source mapping does not match the selected native configuration hash.");
        }

        ReadOnlyMemory<byte> configBytes = await ReadPinnedAsync(configuration);
        await ReadPinnedAsync(Property(mapping, "host"));
        JsonElement contentDescriptor = Property(mapping, "content");
        ReadOnlyMemory<byte> contentBytes = await ReadPinnedAsync(contentDescriptor);
        using var nativeConfiguration = JsonDocument.Parse(configBytes);
        if (Text(nativeConfiguration.RootElement, "identity") != entry.Identity)
        {
            throw Invalid("The mapped Node configuration names another native identity.");
        }

        Dictionary<string, ReadOnlyMemory<byte>> sources = new(StringComparer.Ordinal);
        string[] paths = [TemplatesPath, PromptsPath, ResourcesPath, BindingsPath];
        foreach (string path in paths)
        {
            JsonElement[] descriptors = [.. Array(root, "sources").Where(source => Text(source, "path") == path)];
            if (descriptors.Length != 1)
            {
                throw Invalid($"The Node provenance must declare exactly one owned source '{path}'.");
            }
            sources.Add(path, await ReadPinnedAsync(descriptors[0]));
        }

        NewTemplate[] templates = JsonSerializer.Deserialize<NewTemplate[]>(sources[TemplatesPath].Span)
            ?? throw Invalid("The owned Node templates source is null.");
        UserPromptDoc[] prompts = JsonSerializer.Deserialize<UserPromptDoc[]>(sources[PromptsPath].Span)
            ?? throw Invalid("The owned Node prompts source is null.");
        Dictionary<string, string> resources = JsonSerializer.Deserialize<Dictionary<string, string>>(sources[ResourcesPath].Span)
            ?? throw Invalid("The owned Node resources source is null.");
        if (templates.Any(template => template is null || string.IsNullOrWhiteSpace(template.Id))
            || templates.Select(template => template.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != templates.Length
            || prompts.Any(prompt => prompt is null || string.IsNullOrWhiteSpace(prompt.Id))
            || prompts.Select(prompt => prompt.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != prompts.Length)
        {
            throw Invalid("The owned Node source contains missing or duplicate template/prompt identities.");
        }

        string legacyId = Text(mapping, "legacyId");
        NewTemplate? raw = templates.SingleOrDefault(template => template.Id == legacyId);
        if (raw is null || !entry.ShortNames.Contains(legacyId, StringComparer.Ordinal)
            || !string.Equals(raw.Language, entry.Language, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("The Node legacy identity/language is not declared by the selected native entry.");
        }

        using var bindings = JsonDocument.Parse(sources[BindingsPath]);
        Array(bindings.RootElement, legacyId);
        Dictionary<string, UserPromptDoc> indexedPrompts = prompts.ToDictionary(prompt => prompt.Id!, StringComparer.OrdinalIgnoreCase);
        V2Payload payload = new(ManifestPath, templates, indexedPrompts, resources);
        FunctionTemplateInfo projected = V2TemplateProjection.Project(raw, payload, "node")
            ?? throw Invalid("The selected original Node template cannot be projected.");
        V2Input[] nameInputs = [.. (raw.Jobs ?? []).SelectMany(job => job.Inputs ?? [])
            .Where(input => input.AssignTo == "$(FUNCTION_NAME_INPUT)")];
        if (nameInputs.Length != 1 || nameInputs[0].ParamId is not { } namePromptId
            || !indexedPrompts.ContainsKey(namePromptId) || projected.DefaultFunctionName != entry.DefaultName
            || projected.DisplayName != entry.Name)
        {
            throw Invalid("The selected Node entry differs from its original name/default/prompt source.");
        }

        TemplateUserPrompt functionName = projected.Metadata.UserPrompts.Single(prompt => prompt.Id == namePromptId);
        NativeTemplateValidationRule[] rules = [.. projected.Metadata.UserPrompts
            .Where(prompt => prompt.Id != namePromptId).Select(prompt => new NativeTemplateValidationRule(prompt.Id, prompt))];
        foreach (NativeTemplateValidationRule rule in rules)
        {
            TemplateParameterDefinition? parameter = entry.Parameters.SingleOrDefault(parameter => parameter.CanonicalName == rule.CanonicalName);
            if (parameter is null || parameter.DefaultValue != rule.Prompt.DefaultValue || parameter.IsRequired != rule.Prompt.IsRequired
                || parameter.DataType != rule.Prompt.DataType || !parameter.Choices.Select(choice => choice.Value).SequenceEqual(rule.Prompt.Choices ?? []))
            {
                throw Invalid($"Original Node prompt '{rule.Prompt.Id}' differs from the selected native parameter.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        NativeTemplateLegacyContract contract = new(new(entry.Identity, ManifestPath, Convert.ToHexString(SHA256.HashData(manifestBytes.Span))),
            package, projected.DefaultFunctionName, functionName, null, rules);
        NodeItemSource qualified = new(contract, raw, configBytes, contentBytes, Text(contentDescriptor, "path"), Text(configuration, "path"),
            pins.AsReadOnly(), pins.Single(pin => pin.Identity == Text(contentDescriptor, "path")));
        NodeItemSourceQualifier.Validate(qualified, entry.Language);
        return qualified;

        async Task<ReadOnlyMemory<byte>> ReadPinnedAsync(JsonElement descriptor)
        {
            ReadOnlyMemory<byte> bytes = await ReadVerifiedAsync(selected, descriptor, cancellationToken);
            pins.Add(new(entry.Identity, Text(descriptor, "path"), Convert.ToHexString(SHA256.HashData(bytes.Span))));
            return bytes;
        }
    }

    internal static async Task<ReadOnlyMemory<byte>> ReadVerifiedAsync(ResolvedTemplate selected, JsonElement descriptor, CancellationToken cancellationToken)
    {
        string path = Text(descriptor, "path");
        string expected = Text(descriptor, "sha256");
        if (Path.IsPathRooted(path) || path.Contains('\\') || path.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw Invalid("Node provenance requires exact package-relative source paths.");
        }
        ReadOnlyMemory<byte> bytes = await selected.ReadPackageFileAsync(path, cancellationToken);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes.Span)), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid($"The selected Node owned source '{path}' is missing or changed (SHA-256 mismatch).");
        }
        return bytes;
    }

    internal static JsonElement Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value))
        {
            throw Invalid($"Node provenance is missing '{name}'.");
        }
        return value;
    }

    internal static string Text(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.String } value
        && value.GetString() is { Length: > 0 } text ? text : throw Invalid($"Node provenance '{name}' must be a nonempty string.");

    internal static IEnumerable<JsonElement> Array(JsonElement element, string name) => Property(element, name) is { ValueKind: JsonValueKind.Array } value
        ? value.EnumerateArray() : throw Invalid($"Node provenance '{name}' must be an array.");

    private static InvalidDataException Invalid(string message) => new(message);

    private static NativeItemCompatibilityResult Failed(Exception exception, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(null, "unresolved", [$"Cannot qualify the selected Node item's owned compatibility source: {exception.Message} Nothing was generated."]);
    }
}

internal sealed record NodeItemSource(NativeTemplateLegacyContract Contract, NewTemplate Template, ReadOnlyMemory<byte> Configuration,
    ReadOnlyMemory<byte> Content, string ContentPath, string ConfigurationPath, IReadOnlyList<TemplateEffectSource> Sources, TemplateEffectSource ContentSource);
