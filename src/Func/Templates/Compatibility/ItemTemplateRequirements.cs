// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Projects;

namespace Azure.Functions.Cli.Templates.Compatibility;

/// <summary>
/// Captures neutral capability requirements, preserving absent or invalid declarations separately from a valid nonempty conjunction.
/// The current consumer checks item templates against an existing resolved project.
/// </summary>
internal sealed class ItemTemplateRequirements
{
    public const string Vocabulary = "func-project-capabilities";

    public ItemTemplateRequirements(IEnumerable<string> capabilityIds)
        : this(capabilityIds, isDeclared: true)
    {
    }

    private ItemTemplateRequirements(IEnumerable<string> capabilityIds, bool isDeclared)
    {
        ArgumentNullException.ThrowIfNull(capabilityIds);
        string[] captured = [.. capabilityIds];
        if (isDeclared && captured.Length == 0)
        {
            throw new FormatException("An item requirement declaration must contain at least one capability ID.");
        }

        HashSet<string> unique = new(StringComparer.Ordinal);
        foreach (string id in captured)
        {
            if (!ProjectCapability.IsValidId(id))
            {
                throw new FormatException("An item capability ID must start with an ASCII letter or digit and contain only ASCII letters, digits, '.', '-' or '_'.");
            }

            if (!unique.Add(id))
            {
                throw new FormatException($"The item requirement declaration repeats capability ID '{id}'.");
            }
        }

        Array.Sort(captured, StringComparer.Ordinal);
        IsDeclared = isDeclared;
        CapabilityIds = Array.AsReadOnly(captured);
    }

    private ItemTemplateRequirements(string validationError)
    {
        IsDeclared = true;
        CapabilityIds = Array.AsReadOnly<string>([]);
        ValidationError = validationError;
    }

    public bool IsDeclared { get; }

    public IReadOnlyList<string> CapabilityIds { get; }

    /// <summary>
    /// Gets the parsing failure, or null when the declaration is valid or absent.
    /// </summary>
    public string? ValidationError { get; }

    /// <summary>
    /// Reads a neutral declaration object containing only type and args.
    /// Null denotes absence; malformed or unsupported declarations retain a validation error.
    /// </summary>
    public static ItemTemplateRequirements Parse(string? declaration)
    {
        if (declaration is null)
        {
            return new([], isDeclared: false);
        }

        try
        {
            using var document = JsonDocument.Parse(declaration);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("An item requirement declaration must be an object.");
            }

            HashSet<string> properties = new(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
            {
                string propertyName = ReadPropertyName(property);
                if (!properties.Add(propertyName))
                {
                    throw new FormatException($"The item requirement declaration repeats property '{propertyName}'.");
                }

                if (propertyName is not "type" and not "args")
                {
                    throw new NotSupportedException("Item requirements support only 'type' and 'args'; expressions and alternatives are not supported.");
                }
            }

            if (!root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("An item requirement declaration requires a string 'type'.");
            }

            if (ReadString(type) != Vocabulary)
            {
                throw new NotSupportedException($"Only the '{Vocabulary}' item requirement vocabulary is supported.");
            }

            if (!root.TryGetProperty("args", out JsonElement arguments) || arguments.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Item requirement 'args' must be a nonempty array of capability IDs, interpreted as a conjunction.");
            }

            List<string> ids = [];
            foreach (JsonElement argument in arguments.EnumerateArray())
            {
                if (argument.ValueKind != JsonValueKind.String)
                {
                    throw new FormatException("Item requirement arguments must be capability ID strings; predicates and alternative objects are not supported.");
                }

                ids.Add(ReadString(argument));
            }

            return new(ids);
        }
        catch (JsonException exception)
        {
            return new($"The item requirement declaration is not valid JSON: {exception.Message}");
        }
        catch (FormatException exception)
        {
            return new(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return new(exception.Message);
        }
    }

    private static string ReadPropertyName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException exception)
        {
            throw new FormatException("The item requirement declaration has an invalid Unicode property name.", exception);
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
            throw new FormatException("The item requirement declaration has an invalid Unicode string.", exception);
        }
    }
}
