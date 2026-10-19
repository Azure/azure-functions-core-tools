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
/// Retains original Node defaults and validators without reconstructing explicitly native definitions.
/// </summary>
internal sealed class NodeItemCompatibility(IInteractionService interaction, Func<string?>? updateHint = null,
    NodeConvertedSourceAuthority? sourceAuthority = null) : INativeItemCompatibility
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
        return DescribeUpdate(exception.Message);
    }

    internal string DescribeUnsupportedHelpers(NodeItemHelperCompatibilityException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return DescribeUpdate(exception.Message);
    }

    private string DescribeUpdate(string message)
        => message + " "
            + (updateHint?.Invoke() ?? "Explicitly update the installed Node workload that owns this selected template archive, then retry. "
                + "Use 'func workload list' to identify that owner and 'func workload update <owner-package-id> --exact' to update it.")
            + " Nothing was generated; no workload was upgraded automatically.";

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
            if (selected.Entry.NativeGeneration is not null && !await HasLegacyMappingAsync(selected, cancellationToken))
            {
                string? name = requestedName ?? selected.Entry.DefaultName;
                string? diagnostic = NativeTemplateGenerationContract.FunctionNameDiagnostic(name);
                return new(name, requestedName is null ? "authoredDefault" : "explicit", diagnostic is null ? [] : [diagnostic]);
            }
            NodeItemSource source = await ReadSourceAsync(selected, cancellationToken, sourceAuthority);
            if (selected.Entry.NativeGeneration is null)
            {
                NodeItemSourceQualifier.ValidateArguments(source, arguments.ValuesByCanonicalName);
            }
            NativeTemplateLegacyContract contract = source.Contract;
            NativeItemCompatibility compatibility = new(_interaction, _ => contract, async (candidate, path, token) =>
            {
                // Re-read all source bindings before handing original bytes to the shared validator.
                NodeItemSource current = await ReadSourceAsync(candidate, token, sourceAuthority);
                if (candidate.Entry.NativeGeneration is null)
                {
                    NodeItemSourceQualifier.ValidateArguments(current, arguments.ValuesByCanonicalName);
                }
                return await candidate.ReadPackageFileAsync(path, token);
            });
            NativeItemCompatibilityResult resolved = await compatibility.ResolveAsync(selected, arguments, requestedName, canPrompt, cancellationToken);
            if (resolved.Succeeded && NodeItemSourceQualifier.ValidateDaprInvocation(source, resolved.Name) is { } helperDiagnostic)
            {
                return resolved with { Diagnostics = [helperDiagnostic] };
            }
            return resolved;
        }
        catch (NodeItemHelperCompatibilityException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(null, "unresolved", [DescribeUnsupportedHelpers(exception)]);
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

    internal static async Task<NodeItemSource> ReadSourceAsync(ResolvedTemplate selected, CancellationToken cancellationToken,
        NodeConvertedSourceAuthority? sourceAuthority = null)
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
        using JsonDocument manifest = ParseManifest(manifestBytes);
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
        ReadOnlyMemory<byte> hostBytes = await ReadPinnedAsync(Property(mapping, "host"));
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
        Dictionary<string, string> defaults = new(StringComparer.Ordinal);
        if (mapping.TryGetProperty("nativeDefaults", out JsonElement defaultsDescriptor))
        {
            if (Text(defaultsDescriptor, "path") != "provenance/node/native-defaults.json")
            {
                throw Invalid("Native Node defaults must come from their exact owned authoring source.");
            }
            using JsonDocument authoredDefaults = ParseManifest(await ReadPinnedAsync(defaultsDescriptor));
            JsonElement authored = Property(authoredDefaults.RootElement, legacyId);
            if (authored.ValueKind != JsonValueKind.Object || !authored.EnumerateObject().Any())
            {
                throw Invalid("The selected Node item has no authored native default associations.");
            }
            foreach (JsonProperty declaration in authored.EnumerateObject())
            {
                NativeTemplateValidationRule? rule = rules.SingleOrDefault(rule => rule.CanonicalName == declaration.Name);
                JsonElement original = Property(declaration.Value, "sourceDefaultValue");
                JsonElement native = Property(declaration.Value, "defaultValue");
                if (rule is null || declaration.Value.EnumerateObject().Count() != 2
                    || original.ValueKind != JsonValueKind.String || native.ValueKind != JsonValueKind.String
                    || original.GetString() != rule.Prompt.DefaultValue)
                {
                    throw Invalid("A native Node default differs from its owned original prompt association.");
                }
                defaults.Add(declaration.Name, native.GetString()!);
            }
        }
        foreach (NativeTemplateValidationRule rule in rules)
        {
            TemplateParameterDefinition? parameter = entry.Parameters.SingleOrDefault(parameter => parameter.CanonicalName == rule.CanonicalName);
            string? expectedDefault = defaults.TryGetValue(rule.CanonicalName, out string? authoredDefault) ? authoredDefault : rule.Prompt.DefaultValue;
            if (parameter is null || parameter.DefaultValue != expectedDefault || parameter.IsRequired != rule.Prompt.IsRequired
                || parameter.DataType != rule.Prompt.DataType || !parameter.Choices.Select(choice => choice.Value).SequenceEqual(rule.Prompt.Choices ?? []))
            {
                throw Invalid($"Original Node prompt '{rule.Prompt.Id}' differs from the selected native parameter.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        NativeTemplateLegacyContract contract = new(new(entry.Identity, ManifestPath, Convert.ToHexString(SHA256.HashData(manifestBytes.Span))),
            package, projected.DefaultFunctionName, functionName, null, rules);
        NodeItemSource qualified = new(contract, raw, configBytes, contentBytes, Text(contentDescriptor, "path"), Text(configuration, "path"),
            pins.AsReadOnly(), pins.Single(pin => pin.Identity == Text(contentDescriptor, "path")))
        {
            HostConfiguration = hostBytes,
            OriginalTemplates = sources[TemplatesPath],
        };
        if (sourceAuthority?.Matches(qualified) == true)
        {
            return qualified with { HasHostSourceQualification = true };
        }
        // Auxiliary function identities still require their narrow source-shape proof.
        if (entry.NativeGeneration is null || NodeItemSourceQualifier.HasDaprHelper(raw.Id))
        {
            NodeItemSourceQualifier.Validate(qualified, entry.Language);
        }
        return qualified;

        async Task<ReadOnlyMemory<byte>> ReadPinnedAsync(JsonElement descriptor)
        {
            ReadOnlyMemory<byte> bytes = await ReadVerifiedAsync(selected, descriptor, cancellationToken);
            pins.Add(new(entry.Identity, Text(descriptor, "path"), Convert.ToHexString(SHA256.HashData(bytes.Span))));
            return bytes;
        }
    }

    private static async Task<bool> HasLegacyMappingAsync(ResolvedTemplate selected, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = await selected.ReadPackageFileAsync(ManifestPath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Native-only definitions need not claim a historical conversion or validator contract.
            return false;
        }

        using JsonDocument manifest = ParseManifest(bytes);
        JsonElement root = manifest.RootElement;
        if (selected.Entry.Package is not { } package || Text(root, "packageId") != package.Identifier
            || !NuGetVersion.TryParse(Text(root, "packageVersion"), out NuGetVersion? declared)
            || !NuGetVersion.TryParse(package.Version, out NuGetVersion? installed) || declared != installed)
        {
            throw Invalid("Native Node compatibility provenance does not match its selected package/version.");
        }
        JsonElement[] mappings = [.. Array(root, "templates").Where(mapping => Text(mapping, "identity") == selected.Entry.Identity)];
        return mappings.Length <= 1 ? mappings.Length == 1
            : throw Invalid("The selected native identity has multiple original Node compatibility mappings.");
    }

    private static JsonDocument ParseManifest(ReadOnlyMemory<byte> bytes)
    {
        var document = JsonDocument.Parse(bytes);
        try
        {
            ValidateUniqueProperties(document.RootElement);
            return document;
        }
        catch (InvalidDataException)
        {
            document.Dispose();
            throw;
        }
    }

    internal static void ValidateUniqueProperties(JsonElement value)
    {
        try
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw Invalid($"Duplicate Node provenance property '{property.Name}'.");
                    }
                    ValidateUniqueProperties(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    ValidateUniqueProperties(item);
                }
            }
            else if (value.ValueKind == JsonValueKind.String)
            {
                _ = value.GetString();
            }
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("Node provenance contains invalid Unicode text.", exception);
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
    ReadOnlyMemory<byte> Content, string ContentPath, string ConfigurationPath, IReadOnlyList<TemplateEffectSource> Sources, TemplateEffectSource ContentSource)
{
    internal ReadOnlyMemory<byte> HostConfiguration { get; init; }

    internal ReadOnlyMemory<byte> OriginalTemplates { get; init; }

    internal bool HasHostSourceQualification { get; init; }
}
