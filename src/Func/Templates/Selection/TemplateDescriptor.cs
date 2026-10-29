// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.Globalization;
using Azure.Functions.Cli.Templates.Compatibility;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Selection;

/// <summary>
/// Validates and snapshots neutral template metadata without granting execution authority.
/// </summary>
internal sealed class TemplateDescriptor
{
    /// <summary>
    /// Captures metadata and an optional standalone project-capability declaration.
    /// </summary>
    /// <exception cref="InvalidTemplateMetadataException">The neutral metadata or capability declaration is invalid.</exception>
    /// <exception cref="NotSupportedException">The metadata uses an unsupported template or parameter type.</exception>
    public TemplateDescriptor(TemplateCatalogEntry metadata, string? requirementDeclaration = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        RequireText(metadata.Identity, "Template identity");
        RequireText(metadata.Name, $"Template '{metadata.Identity}' name");
        if (!Enum.IsDefined(metadata.Type))
        {
            throw new NotSupportedException($"Template '{metadata.Identity}' has an unsupported template type.");
        }

        if (metadata.MetadataDiagnostic is not null)
        {
            throw new InvalidTemplateMetadataException($"Template '{metadata.Identity}' has invalid metadata: {metadata.MetadataDiagnostic}");
        }

        if (metadata.GroupIdentity is not null)
        {
            RequireText(metadata.GroupIdentity, $"Template '{metadata.Identity}' group identity");
        }

        if (metadata.Language is not null)
        {
            RequireText(metadata.Language, $"Template '{metadata.Identity}' language");
        }

        if (metadata.Package is { } package)
        {
            RequireText(package.Identifier, "Package identifier");
            if (package.Version is not null)
            {
                RequireText(package.Version, "Package version");
            }
        }

        if (metadata.ShortNames is null || metadata.Parameters is null)
        {
            throw new InvalidTemplateMetadataException($"Template '{metadata.Identity}' requires short-name and parameter collections.");
        }

        ImmutableArray<string> shortNames = [.. metadata.ShortNames];
        HashSet<string> uniqueShortNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (string shortName in shortNames)
        {
            RequireText(shortName, $"Template '{metadata.Identity}' short name");
            if (!uniqueShortNames.Add(shortName))
            {
                throw new InvalidTemplateMetadataException($"Template '{metadata.Identity}' repeats short name '{shortName}'.");
            }
        }

        Metadata = metadata with { ShortNames = shortNames, Parameters = CaptureParameters(metadata) };
        Requirements = ItemTemplateRequirements.Parse(requirementDeclaration);
        if (Requirements.ValidationError is string error)
        {
            throw new InvalidTemplateMetadataException($"Template '{metadata.Identity}' has invalid project capability requirements: {error}");
        }
    }

    public TemplateCatalogEntry Metadata { get; }

    public ItemTemplateRequirements Requirements { get; }

    internal static bool IsValidAlias(string? alias)
        => !string.IsNullOrEmpty(alias)
            && !alias.StartsWith('-')
            && !alias.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is ':' or '=');

    internal static bool TryConvertValue(TemplateParameterDefinition parameter, string value, out string canonicalValue)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(value);

        canonicalValue = value;
        switch (parameter.DataType)
        {
            case "string":
                return true;
            case "bool":
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    canonicalValue = value.ToLowerInvariant();
                    return true;
                }

                return false;
            case "int":
                if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int integer))
                {
                    canonicalValue = integer.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                return false;
            case "float":
                const NumberStyles FloatStyles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
                if (double.TryParse(value, FloatStyles, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
                {
                    canonicalValue = number.ToString("R", CultureInfo.InvariantCulture);
                    return true;
                }

                return false;
            case "choice":
                TemplateParameterChoice? choice = parameter.Choices.SingleOrDefault(
                    choice => choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (choice is not null)
                {
                    canonicalValue = choice.Value;
                    return true;
                }

                return false;
            default:
                throw new NotSupportedException($"Parameter '{parameter.CanonicalName}' has unsupported data type '{parameter.DataType}'.");
        }
    }

    private static ImmutableArray<TemplateParameterDefinition> CaptureParameters(TemplateCatalogEntry metadata)
    {
        ImmutableArray<TemplateParameterDefinition>.Builder parameters = ImmutableArray.CreateBuilder<TemplateParameterDefinition>();
        HashSet<string> canonicalNames = new(StringComparer.Ordinal);
        HashSet<string> longNames = new(StringComparer.Ordinal);
        HashSet<string> shortNames = new(StringComparer.Ordinal);
        foreach (TemplateParameterDefinition parameter in metadata.Parameters)
        {
            if (parameter is null)
            {
                throw new InvalidTemplateMetadataException($"Template '{metadata.Identity}' contains a null parameter.");
            }

            string source = $"Template '{metadata.Identity}' parameter '{parameter.CanonicalName}'";
            RequireText(parameter.CanonicalName, $"{source} canonical name");
            if (!IsValidAlias(parameter.LongName) || (parameter.ShortName is { Length: > 0 } shortName && !IsValidAlias(shortName)))
            {
                throw new InvalidTemplateMetadataException($"{source} has an invalid alias. Aliases cannot start with '-' or contain whitespace, control characters, ':' or '='.");
            }

            if (!canonicalNames.Add(parameter.CanonicalName)
                || !longNames.Add(parameter.LongName)
                || (parameter.ShortName is { Length: > 0 } alias && !shortNames.Add(alias)))
            {
                throw new InvalidTemplateMetadataException($"{source} repeats a canonical name or parameter alias.");
            }

            RequireText(parameter.DataType, $"{source} data type");
            if (parameter.DataType is not ("string" or "bool" or "int" or "float" or "choice"))
            {
                throw new NotSupportedException($"{source} has unsupported data type '{parameter.DataType}'.");
            }

            if (parameter.Choices is null)
            {
                throw new InvalidTemplateMetadataException($"{source} requires a choice collection.");
            }

            ImmutableArray<TemplateParameterChoice> choices = [.. parameter.Choices];
            if ((parameter.DataType == "choice") != (choices.Length > 0))
            {
                throw new InvalidTemplateMetadataException($"{source} must declare choices exactly when its data type is 'choice'.");
            }

            HashSet<string> uniqueChoices = new(StringComparer.OrdinalIgnoreCase);
            foreach (TemplateParameterChoice choice in choices)
            {
                if (choice is null)
                {
                    throw new InvalidTemplateMetadataException($"{source} contains a null choice.");
                }

                RequireText(choice.Value, $"{source} choice");
                if (!uniqueChoices.Add(choice.Value))
                {
                    throw new InvalidTemplateMetadataException($"{source} repeats choice '{choice.Value}'.");
                }
            }

            TemplateParameterDefinition captured = parameter with
            {
                ShortName = string.IsNullOrEmpty(parameter.ShortName) ? null : parameter.ShortName,
                Choices = choices,
            };
            if (captured.DefaultValue is { } defaultValue && !TryConvertValue(captured, defaultValue, out _))
            {
                throw new InvalidTemplateMetadataException($"{source} has an invalid authored default for data type '{captured.DataType}'.");
            }

            if (captured.IsRequired && captured.IsHidden && captured.DefaultValue is null)
            {
                throw new InvalidTemplateMetadataException($"{source} is required and hidden without a default. Correct the package metadata.");
            }

            parameters.Add(captured);
        }

        return parameters.ToImmutable();
    }

    private static void RequireText(string? value, string source)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidTemplateMetadataException($"{source} must be a nonblank string.");
        }
    }
}
