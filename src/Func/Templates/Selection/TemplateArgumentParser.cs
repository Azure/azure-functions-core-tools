// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates.Selection;

/// <summary>
/// Parses one candidate's exact token sequence against an isolated alias schema.
/// </summary>
internal sealed class TemplateArgumentParser
{
    /// <summary>
    /// Returns canonical values, explicit-input provenance, errors, and unresolved required parameters.
    /// </summary>
    /// <exception cref="InvalidTemplateMetadataException">A required fallback alias cannot be assigned unambiguously.</exception>
    public TemplateArgumentParseResult Parse(
        TemplateDescriptor template,
        ImmutableArray<string> tokens,
        ImmutableArray<string> reservedAliases)
    {
        ArgumentNullException.ThrowIfNull(template);
        ValidateInputs(tokens, reservedAliases);

        HashSet<string> reserved = new(reservedAliases, StringComparer.Ordinal);
        HashSet<string> assigned = new(reserved, StringComparer.Ordinal);
        Dictionary<string, TemplateParameterDefinition> bindings = new(StringComparer.Ordinal);
        ImmutableDictionary<string, ImmutableArray<string>>.Builder aliases = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        foreach (TemplateParameterDefinition parameter in template.Metadata.Parameters)
        {
            ImmutableArray<string> effectiveAliases = AssignAliases(parameter, reserved, assigned);
            foreach (string alias in effectiveAliases)
            {
                bindings.Add(alias, parameter);
            }

            aliases.Add(parameter.CanonicalName, effectiveAliases);
        }

        ImmutableArray<string>.Builder diagnostics = ImmutableArray.CreateBuilder<string>();
        ImmutableDictionary<string, string>.Builder values = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        ImmutableHashSet<string>.Builder explicitParameters = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        ImmutableArray<TemplateParameterDefinition>.Builder missing = ImmutableArray.CreateBuilder<TemplateParameterDefinition>();
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];
            int separator = token.IndexOf('=');
            string alias = separator >= 0 ? token[..separator] : token;
            if (!bindings.TryGetValue(alias, out TemplateParameterDefinition? parameter))
            {
                diagnostics.Add($"Unknown template argument '{token}'. Use an assigned template parameter alias.");
                continue;
            }

            string? rawValue = separator >= 0 ? token[(separator + 1)..] : null;
            if (separator < 0)
            {
                if (index + 1 < tokens.Length
                    && (!tokens[index + 1].StartsWith('-')
                        || (parameter.DataType is "int" or "float"
                            && !assigned.Contains(tokens[index + 1])
                            && TemplateDescriptor.TryConvertValue(parameter, tokens[index + 1], out _))))
                {
                    rawValue = tokens[++index];
                }
                else if (parameter.DataType == "bool")
                {
                    rawValue = "true";
                }
            }

            if (!explicitParameters.Add(parameter.CanonicalName))
            {
                values.Remove(parameter.CanonicalName);
                diagnostics.Add($"Parameter '{parameter.CanonicalName}' was supplied more than once. Supply it through only one alias, once.");
                continue;
            }

            if (rawValue is null)
            {
                diagnostics.Add($"Parameter '{parameter.CanonicalName}' requires a value through '{alias}'. Use '{alias}=<value>' for a literal value starting with '-'.");
            }
            else if (TemplateDescriptor.TryConvertValue(parameter, rawValue, out string canonicalValue))
            {
                values.Add(parameter.CanonicalName, canonicalValue);
            }
            else
            {
                diagnostics.Add($"Parameter '{parameter.CanonicalName}' requires a valid '{parameter.DataType}' value through '{aliases[parameter.CanonicalName][0]}'.");
            }
        }

        foreach (TemplateParameterDefinition parameter in template.Metadata.Parameters.Where(
            parameter => !explicitParameters.Contains(parameter.CanonicalName)))
        {
            if (parameter.DefaultValue is { } defaultValue)
            {
                values.Add(parameter.CanonicalName, defaultValue);
            }
            else if (parameter.IsRequired)
            {
                missing.Add(parameter);
            }
        }

        return new(template, values.ToImmutable(), explicitParameters.ToImmutable(), aliases.ToImmutable(), missing.ToImmutable(), diagnostics.ToImmutable());
    }

    internal static void ValidateInputs(ImmutableArray<string> tokens, ImmutableArray<string> reservedAliases)
    {
        if (tokens.IsDefault || tokens.Any(token => token is null))
        {
            throw new ArgumentException("Template tokens must be initialized and cannot contain null.", nameof(tokens));
        }

        if (reservedAliases.IsDefault || reservedAliases.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Reserved aliases must be initialized and cannot contain blank values.", nameof(reservedAliases));
        }
    }

    private static ImmutableArray<string> AssignAliases(
        TemplateParameterDefinition parameter,
        HashSet<string> reserved,
        HashSet<string> assigned)
    {
        string longAlias = $"--{parameter.LongName}";
        if (reserved.Contains(longAlias))
        {
            if (!TemplateDescriptor.IsValidAlias(parameter.CanonicalName))
            {
                throw new InvalidTemplateMetadataException($"Parameter '{parameter.CanonicalName}' cannot form a valid '--param:' fallback alias.");
            }

            longAlias = $"--param:{parameter.CanonicalName}";
        }

        string? shortAlias = parameter.ShortName is { } shortName ? $"-{shortName}" : null;
        if (shortAlias is not null && reserved.Contains(shortAlias))
        {
            shortAlias = $"-p:{parameter.ShortName}";
        }

        ImmutableArray<string> aliases = shortAlias is null ? [longAlias] : [longAlias, shortAlias];
        foreach (string alias in aliases)
        {
            if (!assigned.Add(alias))
            {
                throw new InvalidTemplateMetadataException($"Parameter '{parameter.CanonicalName}' cannot use alias '{alias}' because it is already reserved or assigned.");
            }
        }

        return aliases;
    }
}
