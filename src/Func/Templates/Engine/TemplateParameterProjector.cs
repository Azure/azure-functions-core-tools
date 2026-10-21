// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Projects template engine parameters into command-facing definitions.
/// </summary>
internal static class TemplateParameterProjector
{
    private const string ParameterSymbolType = "parameter";
    private const string NameParameterName = "name";
    private const string DefaultDataType = "string";

    /// <summary>
    /// Returns a definition for each parameter a user can supply, with the template's func host metadata applied.
    /// Uses mounted declarations to distinguish authored defaults from engine fallbacks.
    /// </summary>
    /// <exception cref="InvalidTemplateMetadataException">
    /// The metadata names a symbol users can't set, an alias is invalid or shared, or the configuration is invalid JSON.
    /// </exception>
    public static IReadOnlyList<TemplateParameterDefinition> Project(
        IEnumerable<ITemplateParameter> parameters, FuncHostTemplateMetadata metadata, string? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(metadata);
        return ProjectDefinitions(parameters, metadata, configuration is null ? null : ReadParametersWithoutDefaults(configuration));
    }

    /// <summary>
    /// Projects parameters using the captured host view without reparsing template.json.
    /// </summary>
    public static IReadOnlyList<TemplateParameterDefinition> Project(
        IEnumerable<ITemplateParameter> parameters, FuncHostTemplateMetadata metadata, JsonElement configuration)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(metadata);
        return ProjectDefinitions(parameters, metadata, ReadParametersWithoutDefaults(configuration));
    }

    private static IReadOnlyList<TemplateParameterDefinition> ProjectDefinitions(
        IEnumerable<ITemplateParameter> parameters, FuncHostTemplateMetadata metadata, IReadOnlySet<string>? parametersWithoutDefaults)
    {
        ITemplateParameter[] userParameters = [.. parameters.Where(IsUserParameter)];

        HashSet<string> canonicalNames = [.. userParameters.Select(parameter => parameter.Name)];
        string[] unmatched = [.. metadata.Symbols.Keys.Where(symbol => !canonicalNames.Contains(symbol)).Order(StringComparer.Ordinal)];
        if (unmatched.Length > 0)
        {
            throw new InvalidTemplateMetadataException(
                $"{FuncHostTemplateMetadataReader.FileName} has symbolInfo for {string.Join(", ", unmatched.Select(symbol => $"'{symbol}'"))}, "
                + "which must name parameters users set as options. Commands set 'name' through --name.");
        }

        TemplateParameterDefinition[] definitions = [.. userParameters.Select(parameter => Project(parameter, metadata, parametersWithoutDefaults))];
        EnsureUnique(definitions, definition => definition.LongName, "long name");
        EnsureUnique(definitions, definition => definition.ShortName, "short name");
        return definitions;
    }

    // Commands take the name parameter from --name, whether the engine adds it or the template declares it.
    // The engine also adds an implicit parameter for each tag, which takes no user input.
    private static bool IsUserParameter(ITemplateParameter parameter)
        => parameter.Type == ParameterSymbolType
            && !string.Equals(parameter.Name, NameParameterName, StringComparison.Ordinal)
            && parameter.Precedence.PrecedenceDefinition != PrecedenceDefinition.Implicit;

    private static TemplateParameterDefinition Project(
        ITemplateParameter parameter, FuncHostTemplateMetadata metadata, IReadOnlySet<string>? parametersWithoutDefaults)
    {
        metadata.Symbols.TryGetValue(parameter.Name, out FuncHostSymbolMetadata? symbol);

        string longName = symbol?.LongName ?? parameter.Name;
        EnsureValidAlias(parameter.Name, longName, "long name");
        if (symbol?.ShortName is { } shortName)
        {
            EnsureValidAlias(parameter.Name, shortName, "short name");
        }

        IReadOnlyList<TemplateParameterChoice> choices = parameter.Choices is null
            ? []
            : [.. parameter.Choices.Select(choice => new TemplateParameterChoice(
                choice.Key,
                NullIfEmpty(choice.Value.DisplayName),
                NullIfEmpty(choice.Value.Description)))];

        PrecedenceDefinition precedence = parameter.Precedence.PrecedenceDefinition;

        // The engine leaves an omitted datatype null until it reloads its cache as string.
        string dataType = string.IsNullOrEmpty(parameter.DataType) ? DefaultDataType : parameter.DataType;

        return new(
            parameter.Name,
            longName,
            symbol?.ShortName,
            NullIfEmpty(parameter.Description),
            dataType,
            choices,
            parametersWithoutDefaults?.Contains(parameter.Name) == true ? null : parameter.DefaultValue,
            precedence == PrecedenceDefinition.Required,
            (symbol?.IsHidden ?? false) || precedence == PrecedenceDefinition.Disabled,
            symbol?.AlwaysShow ?? false);
    }

    private static IReadOnlySet<string>? ReadParametersWithoutDefaults(string configuration)
    {
        try
        {
            using var document = JsonDocument.Parse(configuration,
                new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return ReadParametersWithoutDefaults(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidTemplateMetadataException("Template configuration is not valid JSON.", exception);
        }
    }

    private static IReadOnlySet<string>? ReadParametersWithoutDefaults(JsonElement configuration)
    {
        if (configuration.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidTemplateMetadataException("Template configuration must contain a JSON object.");
        }

        // Additional configuration files can supply defaults absent from the primary declaration.
        if (configuration.EnumerateObject().Any(property => property.Name.Equals("additionalConfigFiles", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        // Engine-synthesized scalar defaults must not become user input and override generated fallbacks.
        return configuration.EnumerateObject()
            .Where(property => property.Name.Equals("symbols", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
            .SelectMany(property => property.Value.EnumerateObject())
            .Where(symbol => symbol.Value.ValueKind == JsonValueKind.Object && symbol.Value.EnumerateObject()
                .All(property => !property.Name.Equals("defaultValue", StringComparison.OrdinalIgnoreCase) || property.Value.ValueKind == JsonValueKind.Null))
            .Select(symbol => symbol.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    // The parser adds the dashes, and ':' and '=' separate an option from its value.
    private static void EnsureValidAlias(string parameterName, string alias, string kind)
    {
        if (alias.StartsWith('-') || alias.Any(character => char.IsWhiteSpace(character) || character is ':' or '='))
        {
            throw new InvalidTemplateMetadataException(
                $"Parameter '{parameterName}' has an invalid {kind} '{alias}'. Aliases can't start with '-' or contain whitespace, ':' or '='. "
                + $"Set a valid {kind} in {FuncHostTemplateMetadataReader.FileName}.");
        }
    }

    private static void EnsureUnique(
        IEnumerable<TemplateParameterDefinition> definitions,
        Func<TemplateParameterDefinition, string?> alias,
        string kind)
    {
        IGrouping<string, TemplateParameterDefinition>? shared = definitions
            .Where(definition => alias(definition) is not null)
            .GroupBy(definition => alias(definition)!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (shared is not null)
        {
            string names = string.Join(" and ", shared.Select(definition => $"'{definition.CanonicalName}'"));
            throw new InvalidTemplateMetadataException(
                $"Parameters {names} share the {kind} '{shared.Key}'. Give each a different {kind} in {FuncHostTemplateMetadataReader.FileName}.");
        }
    }

    // The engine stores missing descriptions and display names as empty strings.
    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
