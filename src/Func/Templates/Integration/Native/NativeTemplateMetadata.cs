// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text.Json;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Integration.Native;

/// <summary>
/// Validates original package metadata before native parsing can discard declarations or duplicate keys.
/// </summary>
internal sealed class NativeTemplateMetadata
{
    internal static readonly Guid ConfigurationActionId = new("00e33184-ffc7-43ef-bec0-b1684df8ad56");
    private const string CapabilityField = "funcProjectCapabilities";
    private const string InternalRoleTag = "func-role";

    public NativeTemplateMetadata(string path, string configuration, string? hostConfiguration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(configuration);
        Path = path;
        using JsonDocument document = Parse(configuration, path);
        JsonElement root = document.RootElement;
        RequireObject(root, path);
        ValidateKeys(root, path);
        Identity = RequireText(root, "identity", path);
        RequireText(root, "name", path);
        ValidateShortNames(root, path);
        if (root.TryGetProperty("precedence", out JsonElement precedence)
            && (precedence.ValueKind != JsonValueKind.Number || !precedence.TryGetInt32(out _)))
        {
            throw Invalid(path, "Authored precedence must be an integer.");
        }

        if (!root.TryGetProperty("tags", out JsonElement tags))
        {
            throw Invalid(path, "Declare tags.type as 'project' or 'item'.");
        }

        RequireObject(tags, path + ".tags");
        Type = RequireText(tags, "type", path) switch
        {
            string type when type.Equals("project", StringComparison.OrdinalIgnoreCase) => TemplateType.Project,
            string type when type.Equals("item", StringComparison.OrdinalIgnoreCase) => TemplateType.Item,
            _ => throw Invalid(path, "Only project and item template types are supported."),
        };
        foreach (JsonProperty tag in tags.EnumerateObject())
        {
            if (!tag.Name.Equals(InternalRoleTag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tag.Name != InternalRoleTag)
            {
                throw Invalid(path, "tags.func-role must use the exact property casing.");
            }

            string role = RequireText(tags, InternalRoleTag, path + ".tags");
            if (role.Trim() != role)
            {
                throw Invalid(path, "tags.func-role must be a nonempty, trimmed string.");
            }

            DeclaredInternalRole = role;
        }

        string[] optionalText = ["groupIdentity", "language"];
        foreach (string property in optionalText)
        {
            JsonElement owner = property == "language" ? tags : root;
            if (owner.TryGetProperty(property, out _))
            {
                RequireText(owner, property, path);
            }
        }

        if (root.TryGetProperty("defaultName", out _))
        {
            DefaultName = RequireText(root, "defaultName", path);
        }

        JsonProperty[] carriers = [.. root.EnumerateObject().Where(property =>
            property.Name.Equals(CapabilityField, StringComparison.OrdinalIgnoreCase))];
        if (carriers.Length != 0)
        {
            if (Type != TemplateType.Item)
            {
                throw Invalid(path, $"{CapabilityField} is item-only and is forbidden on project templates.");
            }

            if (carriers[0].Name != CapabilityField)
            {
                throw new NotSupportedException($"{path}: use the exact '{CapabilityField}' field; alternate carrier casing is unsupported.");
            }

            if (carriers[0].Value.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(path, $"{CapabilityField} must be an object containing type and args.");
            }

            RequirementDeclaration = carriers[0].Value.GetRawText();
            var requirements = ItemTemplateRequirements.Parse(RequirementDeclaration);
            if (requirements.ValidationError is string error)
            {
                throw new InvalidTemplateMetadataException($"{path}: invalid {CapabilityField}: {error}");
            }
        }

        Constraints = TemplateConstraintDeclarationReader.Read(configuration);
        ParameterDefaults = ReadSymbols(root, path);
        HostMetadata = FuncHostTemplateMetadata.Empty;
        if (hostConfiguration is not null)
        {
            string hostPath = path.Replace("template.json", FuncHostTemplateMetadataReader.FileName, StringComparison.Ordinal);
            using JsonDocument host = Parse(hostConfiguration, hostPath);
            RequireObject(host.RootElement, hostPath);
            ValidateKeys(host.RootElement, hostPath);
            if (host.RootElement.TryGetProperty("symbolInfo", out JsonElement symbols))
            {
                RequireObject(symbols, hostPath + ".symbolInfo");
                foreach (JsonProperty symbol in symbols.EnumerateObject())
                {
                    RequireObject(symbol.Value, hostPath + "." + symbol.Name);
                    if (symbol.Value.TryGetProperty("longName", out _))
                    {
                        RequireText(symbol.Value, "longName", hostPath);
                    }

                    if (symbol.Value.TryGetProperty("shortName", out JsonElement shortName)
                        && shortName.ValueKind != JsonValueKind.Null)
                    {
                        RequireText(symbol.Value, "shortName", hostPath);
                    }
                }
            }

            HostMetadata = FuncHostTemplateMetadataReader.Read(hostPath, hostConfiguration);
        }

        if (root.TryGetProperty("sources", out JsonElement sources))
        {
            if (sources.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(path, "sources must be an array.");
            }

            foreach (JsonElement source in sources.EnumerateArray())
            {
                RequireObject(source, path + ".sources");
                ValidateSourceTargets(source, path);
            }
        }

        Configuration = ReadConfiguration(root, path, Type);
    }

    public string Path { get; }

    public string Identity { get; }

    public TemplateType Type { get; }

    public string? DeclaredInternalRole { get; }

    public string? RequirementDeclaration { get; }

    public string? DefaultName { get; }

    public IReadOnlyList<TemplateConstraintDeclaration> Constraints { get; }

    public IReadOnlyDictionary<string, string?> ParameterDefaults { get; }

    public FuncHostTemplateMetadata HostMetadata { get; }

    public NativeProjectConfiguration? Configuration { get; }

    private static JsonDocument Parse(string text, string source)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException($"{source}: metadata is not valid JSON. Correct the original package metadata.", exception);
        }
    }

    private static void ValidateKeys(JsonElement value, string source, bool identifierMap = false)
    {
        try
        {
            ValidateKeysCore(value, source, identifierMap);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException($"{source}: metadata contains an invalid decoded Unicode name or string.", exception);
        }
    }

    private static void ValidateKeysCore(JsonElement value, string source, bool identifierMap)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(identifierMap ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw Invalid(source, $"Duplicate or ambiguous decoded property '{property.Name}'.");
                }

                ValidateKeys(property.Value, source + "." + property.Name,
                    property.Name is "symbols" or "symbolInfo" or "constraints" or "rename" or "forms" or "baselines");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement element in value.EnumerateArray())
            {
                ValidateKeys(element, source);
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            _ = value.GetString();
        }
    }

    private static IReadOnlyDictionary<string, string?> ReadSymbols(JsonElement root, string source)
    {
        Dictionary<string, string?> defaults = new(StringComparer.Ordinal);
        if (!root.TryGetProperty("symbols", out JsonElement symbols))
        {
            return defaults;
        }

        RequireObject(symbols, source + ".symbols");
        foreach (JsonProperty symbol in symbols.EnumerateObject())
        {
            RequireObject(symbol.Value, source + ".symbols." + symbol.Name);
            string type = RequireText(symbol.Value, "type", source);
            if (type != "parameter")
            {
                continue;
            }

            if (symbol.Value.TryGetProperty("datatype", out _))
            {
                RequireText(symbol.Value, "datatype", source);
            }

            if (symbol.Value.TryGetProperty("choices", out JsonElement choices))
            {
                if (choices.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(source, $"Parameter '{symbol.Name}' choices must be an array.");
                }

                HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement choice in choices.EnumerateArray())
                {
                    RequireObject(choice, source + ".choices");
                    string value = RequireText(choice, "choice", source);
                    if (!values.Add(value))
                    {
                        throw Invalid(source, $"Parameter '{symbol.Name}' repeats choice '{value}' before native normalization.");
                    }
                }
            }

            if (symbol.Value.TryGetProperty("defaultValue", out JsonElement defaultValue))
            {
                if (defaultValue.ValueKind != JsonValueKind.String)
                {
                    throw Invalid(source, $"Parameter '{symbol.Name}' defaultValue must be a string, including an explicit empty string.");
                }

                defaults.Add(symbol.Name, defaultValue.GetString());
            }

            if (symbol.Value.TryGetProperty("isRequired", out JsonElement required)
                && required.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            {
                throw new NotSupportedException($"{source}: conditional required parameters are outside this package profile.");
            }
        }

        return defaults;
    }

    private static void ValidateShortNames(JsonElement root, string source)
    {
        if (!root.TryGetProperty("shortName", out JsonElement aliases))
        {
            throw Invalid(source, "Declare a native shortName string or nonempty array.");
        }

        JsonElement[] names = aliases.ValueKind == JsonValueKind.Array ? [.. aliases.EnumerateArray()] : [aliases];
        HashSet<string> unique = new(StringComparer.OrdinalIgnoreCase);
        if (names.Length == 0)
        {
            throw Invalid(source, "Native shortName cannot be an empty array.");
        }

        foreach (JsonElement name in names)
        {
            if (name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()) || !unique.Add(name.GetString()!))
            {
                throw Invalid(source, "Native shortName values must be nonempty unique strings.");
            }
        }
    }

    private static NativeProjectConfiguration? ReadConfiguration(JsonElement root, string source, TemplateType type)
    {
        JsonElement[] actions = root.TryGetProperty("postActions", out JsonElement authoredActions)
            ? authoredActions.ValueKind == JsonValueKind.Array
                ? [.. authoredActions.EnumerateArray()]
                : throw Invalid(source, "postActions must be an array.")
            : [];
        if (type == TemplateType.Item)
        {
            if (actions.Length != 0)
            {
                throw new NotSupportedException($"{source}: item post-actions are not supported by this create-only package path.");
            }

            return null;
        }

        if (actions.Length != 1)
        {
            throw Invalid(source, "A project requires exactly one mandatory Functions configuration action and no executable post-actions.");
        }

        JsonElement action = actions[0];
        RequireObject(action, source + ".postActions");
        if (!Guid.TryParse(RequireText(action, "actionId", source), out Guid id) || id != ConfigurationActionId)
        {
            throw new NotSupportedException($"{source}: only the Functions configuration post-action is supported.");
        }

        if (!action.TryGetProperty("continueOnError", out JsonElement continueOnError) || continueOnError.ValueKind != JsonValueKind.False
            || action.TryGetProperty("condition", out _))
        {
            throw Invalid(source, "The single configuration action must be unconditional and declare continueOnError=false.");
        }

        if (!action.TryGetProperty("args", out JsonElement args))
        {
            throw Invalid(source, "The configuration action requires primaryOutputIndex, stack and language.");
        }

        RequireObject(args, source + ".postActions.args");
        if (args.EnumerateObject().Any(property => property.Name is not ("primaryOutputIndex" or "stack" or "language"))
            || !int.TryParse(RequireText(args, "primaryOutputIndex", source), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            throw Invalid(source, "The configuration action requires an authored primaryOutputIndex and only stack/language arguments.");
        }

        if (!root.TryGetProperty("primaryOutputs", out JsonElement outputs) || outputs.ValueKind != JsonValueKind.Array
            || index >= outputs.GetArrayLength())
        {
            throw Invalid(source, "The configuration primaryOutputIndex does not identify an authored primary output.");
        }

        foreach (JsonElement output in outputs.EnumerateArray())
        {
            RequireObject(output, source + ".primaryOutputs");
            RequireText(output, "path", source);
            if (output.TryGetProperty("condition", out _))
            {
                throw new NotSupportedException($"{source}: conditional primary outputs are outside the single-root package profile.");
            }
        }

        return new(index, outputs.GetArrayLength(), RequireText(args, "stack", source), RequireText(args, "language", source));
    }

    private static void ValidateSourceTargets(JsonElement value, string source)
    {
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.Name is "target" or "source")
            {
                string target = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : throw Invalid(source, "A source path must be a string.");
                if (System.IO.Path.IsPathRooted(target))
                {
                    throw Invalid(source, $"Authored {property.Name} path '{target}' must stay relative to the retained package and private output.");
                }

                string logical = target.Replace('\\', '/').TrimEnd('/');
                if (logical is not ("" or "."))
                {
                    try
                    {
                        Preparation.PreparedOutputEntry.NormalizeRelativePath(logical);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new InvalidTemplateMetadataException(
                            $"{source}: authored {property.Name} path '{target}' is invalid. Use a safe relative native path.", exception);
                    }
                }
            }
            else if (property.Name == "modifiers")
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    throw Invalid(source, "Source modifiers must be an array.");
                }

                foreach (JsonElement modifier in property.Value.EnumerateArray())
                {
                    RequireObject(modifier, source + ".modifiers");
                    ValidateSourceTargets(modifier, source);
                }
            }
        }
    }

    private static string RequireText(JsonElement owner, string name, string source)
        => owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
                ? text
                : throw Invalid(source, $"'{name}' must be a nonempty string.");

    private static void RequireObject(JsonElement value, string source)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(source, "Expected a metadata object.");
        }
    }

    private static InvalidTemplateMetadataException Invalid(string source, string message) => new($"{source}: {message}");
}

internal sealed record NativeProjectConfiguration(int PrimaryOutputIndex, int AuthoredOutputCount, string Stack, string Language);
