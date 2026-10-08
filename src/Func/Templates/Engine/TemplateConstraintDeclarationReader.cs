// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using NuGet.Versioning;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Validates raw constraint declarations before engine normalization can discard requirements.
/// </summary>
internal static class TemplateConstraintDeclarationReader
{
    private static readonly JsonDocumentOptions _options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parses declarations and optionally checks NuGet host-sharing metadata supplied by the caller.
    /// </summary>
    /// <exception cref="InvalidTemplateMetadataException">The configuration or a constraint declaration is invalid.</exception>
    public static IReadOnlyList<TemplateConstraintDeclaration> Read(string configuration, IReadOnlyCollection<string>? packageTypes = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        try
        {
            using var document = JsonDocument.Parse(configuration, _options);
            return ReadRoot(document.RootElement, packageTypes);
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException("template.json is not valid JSON.", exception);
        }
    }

    private static IReadOnlyList<TemplateConstraintDeclaration> ReadRoot(JsonElement root, IReadOnlyCollection<string>? packageTypes)
    {
        RequireObject(root, "template.json");
        JsonElement? constraints = FindProperty(root, "constraints", "template.json");
        if (constraints is null)
        {
            return [];
        }

        RequireObject(constraints.Value, "constraints");
        ValidateDuplicates(constraints.Value, "constraints");
        bool shared = packageTypes?.Any(type => string.Equals(type, "Template", StringComparison.OrdinalIgnoreCase)) == true;
        bool project = FindProperty(root, "tags", "template.json") is { ValueKind: JsonValueKind.Object } tags
            && FindProperty(tags, "type", "tags") is { ValueKind: JsonValueKind.String } templateType
            && string.Equals(ReadString(templateType), "project", StringComparison.OrdinalIgnoreCase);
        List<TemplateConstraintDeclaration> declarations = [];
        foreach (JsonProperty property in constraints.Value.EnumerateObject())
        {
            string label = ReadName(property);
            string source = $"constraint '{label}'";
            RequireObject(property.Value, source);
            string type = RequireString(FindProperty(property.Value, "type", source), $"{source} type");
            JsonElement? arguments = FindProperty(property.Value, "args", source);
            bool funcType = type.StartsWith("func-", StringComparison.Ordinal);
            if (shared && funcType)
            {
                throw Invalid($"{source} declares a func type in a package shared with another template host.");
            }

            if (project && type == "func-bundle")
            {
                throw Invalid($"{source} declares func-bundle in a project template.");
            }

            IReadOnlyList<TemplateConstraintAlternative> alternatives = type switch
            {
                "func-workload" => ReadAlternatives(arguments, workload: true, source),
                "func-bundle" => ReadAlternatives(arguments, workload: false, source),
                _ => [],
            };
            declarations.Add(new(label, type, arguments?.Clone(), alternatives));
        }

        return declarations.AsReadOnly();
    }

    private static IReadOnlyList<TemplateConstraintAlternative> ReadAlternatives(JsonElement? arguments, bool workload, string source)
    {
        if (arguments is null)
        {
            throw Invalid($"{source} requires args.");
        }

        if (arguments.Value.ValueKind != JsonValueKind.Array)
        {
            return [ReadAlternative(arguments.Value, workload, source)];
        }

        List<TemplateConstraintAlternative> alternatives = [];
        foreach (JsonElement item in arguments.Value.EnumerateArray())
        {
            alternatives.Add(ReadAlternative(item, workload, source));
        }

        if (alternatives.Count == 0)
        {
            throw Invalid($"{source} requires at least one alternative.");
        }

        return alternatives.AsReadOnly();
    }

    private static TemplateConstraintAlternative ReadAlternative(JsonElement argument, bool workload, string source)
    {
        if (argument.ValueKind == JsonValueKind.String)
        {
            string value = RequireString(argument, $"{source} argument");
            return workload ? new(ReadWorkloadName(value, source), null) : new(null, ReadRange(value, source));
        }

        RequireObject(argument, $"{source} argument");
        foreach (JsonProperty property in argument.EnumerateObject())
        {
            if (!IsKey(property, "id") && !IsKey(property, "version"))
            {
                throw Invalid($"{source} argument has an unsupported property '{ReadName(property)}'.");
            }
        }

        JsonElement? identityProperty = FindProperty(argument, "id", source);
        string? identity = identityProperty is null ? null : RequireString(identityProperty, $"{source} id");
        if (workload)
        {
            identity = ReadWorkloadName(identity ?? throw Invalid($"{source} argument requires id."), source);
        }

        JsonElement? versionProperty = FindProperty(argument, "version", source);
        VersionRange? version = versionProperty is null ? null : ReadRange(RequireString(versionProperty, $"{source} version"), source);
        return new(identity, version);
    }

    private static string ReadWorkloadName(string name, string source)
    {
        if (!char.IsAsciiLetterOrDigit(name[0])
            || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_')
            || name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid($"{source} has an invalid workload name.");
        }

        return name;
    }

    private static VersionRange ReadRange(string value, string source)
        => VersionRange.TryParse(value, out VersionRange? range)
            ? range
            : throw Invalid($"{source} has an invalid version range.");

    private static JsonElement? FindProperty(JsonElement root, string name, string source)
    {
        JsonElement? result = null;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!IsKey(property, name))
            {
                continue;
            }

            if (result is not null)
            {
                throw Invalid($"{source} repeats '{name}'.");
            }

            result = property.Value;
        }

        return result;
    }

    private static void ValidateDuplicates(JsonElement value, string source)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string name = ReadName(property);
                if (!names.Add(name))
                {
                    throw Invalid($"{source} repeats '{name}'.");
                }

                ValidateDuplicates(property.Value, $"{source}.{name}");
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                ValidateDuplicates(item, source);
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            ReadString(value);
        }
    }

    private static string RequireString(JsonElement? value, string source)
        => value is { ValueKind: JsonValueKind.String } element && ReadString(element) is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw Invalid($"{source} must be a non-empty string.");

    private static void RequireObject(JsonElement value, string source)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{source} must be a JSON object.");
        }
    }

    private static string ReadName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException("template.json has an invalid Unicode property name.", exception);
        }
    }

    private static string ReadString(JsonElement value)
    {
        try
        {
            return value.GetString()!;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidTemplateMetadataException("template.json has an invalid Unicode string.", exception);
        }
    }

    private static bool IsKey(JsonProperty property, string name) => string.Equals(ReadName(property), name, StringComparison.OrdinalIgnoreCase);

    private static InvalidTemplateMetadataException Invalid(string message) => new(message);
}