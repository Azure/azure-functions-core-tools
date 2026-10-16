// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Performs strict candidate-specific scalar parsing for both template commands.
/// </summary>
internal sealed class TemplateArgumentParser : ITemplateArgumentParser
{
    public TemplateCandidateParseResult Parse(TemplateArgumentParseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TemplateIdentity);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        ArgumentNullException.ThrowIfNull(request.ReservedAliases);
        ArgumentNullException.ThrowIfNull(request.RawTemplateTokens);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Parameters.Any(parameter => parameter is null) || request.RawTemplateTokens.Any(token => token is null)
            || request.ReservedAliases.Any(alias => alias is null))
        {
            throw new ArgumentException("Definitions, tokens and reserved aliases must not contain null entries.", nameof(request));
        }

        List<TemplateArgumentDiagnostic> invalid = [];
        List<string> definitions = [];
        List<TemplateArgumentParameter> parameters = AssignAliases(request, definitions);
        Dictionary<string, string?> values = new(StringComparer.Ordinal);
        HashSet<string> explicitNames = new(StringComparer.Ordinal);
        List<TemplateArgumentParameter> missing = [];
        if (definitions.Count > 0)
        {
            return Result();
        }

        RootCommand parser = [];
        parser.Options.Clear();
        parser.Directives.Clear();
        Dictionary<TemplateArgumentParameter, Option<string?>> options = [];
        Dictionary<string, string> parserAliases = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> aliasSources = new(StringComparer.Ordinal);
        foreach (TemplateArgumentParameter parameter in parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // System.CommandLine splits colon-bearing fallback aliases before inline values.
            Option<string?> option = new($"--template-parameter-{options.Count}")
            {
                Arity = ArgumentArity.ExactlyOne,
                AllowMultipleArgumentsPerToken = false,
            };
            parserAliases.Add(parameter.LongAlias, option.Name);
            if (parameter.ShortAlias is not null)
            {
                parserAliases.Add(parameter.ShortAlias, option.Name);
            }

            parser.Options.Add(option);
            options.Add(parameter, option);
            aliasSources.Add(option.Name, []);
        }

        string[] aliasesByLength = [.. parserAliases.Keys.OrderByDescending(alias => alias.Length)];
        List<(int SourceIndex, TokenType ExpectedType)> provenance = [];
        string[] parserTokens = [.. request.RawTemplateTokens.SelectMany(MapToken)];
        if (invalid.Count > 0)
        {
            return Result();
        }

        // Supply the root prefix ourselves so the engine never elides a matching token from the user's template tail.
        ParseResult parsed = parser.Parse([parser.Name, .. parserTokens], new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null,
        });
        if (parsed.Tokens.Count != provenance.Count)
        {
            AddInvalid("Template token sequence could not be accounted for safely.", Enumerable.Range(0, request.RawTemplateTokens.Count));
            return Result();
        }

        Dictionary<Token, int> sourceIndices = new(ReferenceEqualityComparer.Instance);
        HashSet<Token> valueTokens = new(ReferenceEqualityComparer.Instance);
        HashSet<string> parsedAliases = new(StringComparer.Ordinal);
        foreach (Option<string?> option in options.Values)
        {
            if (parsed.GetResult(option) is { } result)
            {
                valueTokens.UnionWith(result.Tokens);
                parsedAliases.Add(option.Name);
            }
        }

        HashSet<int> unassigned = [];
        for (int index = 0; index < parsed.Tokens.Count; index++)
        {
            Token token = parsed.Tokens[index];
            (int sourceIndex, TokenType expectedType) = provenance[index];
            sourceIndices.Add(token, sourceIndex);
            if (sourceIndex >= 0 && (token.Type != expectedType
                || (expectedType == TokenType.Argument && !valueTokens.Contains(token))
                || (expectedType == TokenType.Option && !parsedAliases.Contains(token.Value))))
            {
                unassigned.Add(sourceIndex);
            }
        }

        foreach (int index in unassigned.Order())
        {
            AddInvalid($"Unrecognized template token '{request.RawTemplateTokens[index]}'. Supply a declared alias and its value.", [index]);
        }

        foreach (ParseError error in parsed.Errors)
        {
            IEnumerable<int> sources = unassigned;
            for (SymbolResult? symbol = error.SymbolResult; symbol is not null; symbol = symbol.Parent)
            {
                if (symbol is OptionResult option)
                {
                    sources = aliasSources[option.Option.Name];
                    break;
                }
            }

            AddInvalid(DisplayAliases(error.Message), sources);
        }

        if (unassigned.Count > 0)
        {
            return Result();
        }

        foreach ((TemplateArgumentParameter parameter, Option<string?> option) in options)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OptionResult? supplied = parsed.GetResult(option);
            string canonicalName = parameter.Definition.CanonicalName;
            bool hasCompleted = request.CompletedValuesByCanonicalName?.ContainsKey(canonicalName) == true;
            if (supplied is not null)
            {
                explicitNames.Add(canonicalName);
                if (hasCompleted)
                {
                    AddInvalid($"A completed value for '{canonicalName}' cannot replace explicit input for '{parameter.LongAlias}'.", aliasSources[option.Name]);
                }

                if (supplied.IdentifierTokenCount > 1)
                {
                    AddInvalid($"Option '{parameter.LongAlias}' cannot be repeated.", aliasSources[option.Name]);
                }
                else if (supplied.Tokens.Count == 1 && !supplied.Errors.Any())
                {
                    AddValue(parameter, supplied.Tokens[0].Value, [.. aliasSources[option.Name], sourceIndices[supplied.Tokens[0]]]);
                }
                else if (!supplied.Errors.Any())
                {
                    AddInvalid($"Option '{parameter.LongAlias}' requires exactly one explicit value and cannot be repeated.", aliasSources[option.Name]);
                }
            }
            else if (hasCompleted)
            {
                string? completed = request.CompletedValuesByCanonicalName![canonicalName];
                if (completed is not null)
                {
                    AddValue(parameter, completed);
                }
            }
            else if (parameter.Definition.DefaultValue is { } defaultValue)
            {
                // Required defaults must be supplied explicitly to the native engine.
                AddValue(parameter, defaultValue, isDefault: true);
            }

            if (parameter.Definition.IsRequired && !values.ContainsKey(canonicalName))
            {
                missing.Add(parameter);
                if (parameter.Definition.IsHidden && supplied is null && parameter.Definition.DefaultValue is null
                    && (!hasCompleted || request.CompletedValuesByCanonicalName![canonicalName] is null))
                {
                    definitions.Add($"Required parameter '{canonicalName}' is hidden and has no resolved default. Correct the template definition.");
                }
            }
        }

        if (request.CompletedValuesByCanonicalName is not null)
        {
            foreach (string canonicalName in request.CompletedValuesByCanonicalName.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!parameters.Any(parameter => string.Equals(parameter.Definition.CanonicalName, canonicalName, StringComparison.Ordinal)))
                {
                    AddInvalid($"Completed value '{canonicalName}' must name a declared canonical parameter. Supply the template name through the host's --name option.");
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Result();

        TemplateCandidateParseResult Result()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(request.TemplateIdentity, new ReadOnlyDictionary<string, string?>(values), parameters.AsReadOnly(),
                invalid.AsReadOnly(), definitions.AsReadOnly(), missing.AsReadOnly())
            {
                ExplicitParameterNames = explicitNames.ToFrozenSet(StringComparer.Ordinal),
            };
        }

        void AddInvalid(string message, IEnumerable<int>? indices = null)
            => invalid.Add(new(message, Array.AsReadOnly<int>([.. (indices ?? []).Distinct().Order()])));

        void AddValue(TemplateArgumentParameter parameter, string value, IReadOnlyList<int>? indices = null, bool isDefault = false)
        {
            if (IsValidValue(parameter.Definition, value))
            {
                values.Add(parameter.Definition.CanonicalName, value);
            }
            else
            {
                string expectation = string.Equals(parameter.Definition.DataType, "choice", StringComparison.OrdinalIgnoreCase)
                    ? string.Join(", ", parameter.Definition.Choices.Select(choice => $"'{choice.Value}'"))
                    : parameter.Definition.DataType;
                string message = $"Value '{value}' for '{parameter.LongAlias}' is invalid. Expected {expectation}.";
                if (isDefault)
                {
                    definitions.Add(message);
                }
                else
                {
                    AddInvalid(message, indices);
                }
            }
        }

        IReadOnlyList<string> MapToken(string token, int sourceIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (parserAliases.TryGetValue(token, out string? mapped))
            {
                aliasSources[mapped].Add(sourceIndex);
                provenance.Add((sourceIndex, TokenType.Option));
                return [mapped];
            }

            foreach (string alias in aliasesByLength)
            {
                if (token.StartsWith(alias, StringComparison.Ordinal) && token.Length > alias.Length
                    && token[alias.Length] is '=' or ':')
                {
                    aliasSources[parserAliases[alias]].Add(sourceIndex);
                    provenance.Add((sourceIndex, TokenType.Option));
                    provenance.Add((sourceIndex, TokenType.Argument));
                    return token.Length == alias.Length + 1
                        ? [parserAliases[alias], string.Empty]
                        : [parserAliases[alias] + token[alias.Length..]];
                }
            }

            provenance.Add((sourceIndex, TokenType.Argument));
            // Unknown option-like tokens otherwise become string values. Literal dash-prefixed values use inline syntax.
            if (token.StartsWith('-') && !long.TryParse(token, out _)
                && !double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out _)
                && !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                AddInvalid($"Unrecognized template option '{token}'. Use an effective template alias, or inline syntax for a dash-prefixed literal value.", [sourceIndex]);
            }

            return [token];
        }

        string DisplayAliases(string message)
        {
            foreach ((TemplateArgumentParameter parameter, Option<string?> option) in options.OrderByDescending(pair => pair.Value.Name.Length))
            {
                message = message.Replace(option.Name, parameter.LongAlias, StringComparison.Ordinal);
            }

            return message;
        }
    }

    private static List<TemplateArgumentParameter> AssignAliases(TemplateArgumentParseRequest request, List<string> diagnostics)
    {
        HashSet<string> reserved = new(request.ReservedAliases, StringComparer.Ordinal);
        HashSet<string> canonicalNames = new(StringComparer.Ordinal);
        HashSet<string> aliases = new(StringComparer.Ordinal);
        List<TemplateArgumentParameter> parameters = [];
        foreach (TemplateParameterDefinition definition in request.Parameters)
        {
            if (string.IsNullOrWhiteSpace(definition.CanonicalName) || !canonicalNames.Add(definition.CanonicalName))
            {
                diagnostics.Add($"Template '{request.TemplateIdentity}' has an empty or duplicate canonical parameter name. Correct the template definition.");
                continue;
            }

            if (definition.CanonicalName == "name")
            {
                diagnostics.Add("The native 'name' symbol belongs to the host's --name option and must not be projected as a template parameter.");
                continue;
            }

            if (!IsValidAliasName(definition.LongName) || (!string.IsNullOrEmpty(definition.ShortName) && !IsValidAliasName(definition.ShortName)))
            {
                diagnostics.Add($"Parameter '{definition.CanonicalName}' has an invalid projected alias. Correct func.host.json.");
                continue;
            }

            if (!IsSupportedDataType(definition.DataType))
            {
                diagnostics.Add($"Parameter '{definition.CanonicalName}' uses unsupported data type '{definition.DataType}'. A qualified parameter definition is required.");
                continue;
            }

            if (definition.Choices is null || (definition.DataType.Equals("choice", StringComparison.OrdinalIgnoreCase)
                && (definition.Choices.Count == 0 || definition.Choices.Any(choice => choice is null || string.IsNullOrEmpty(choice.Value))
                    || definition.Choices.Select(choice => choice.Value).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definition.Choices.Count)))
            {
                diagnostics.Add($"Parameter '{definition.CanonicalName}' has invalid or ambiguous choices. Correct the template definition.");
                continue;
            }

            string longAlias = $"--{definition.LongName}";
            if (IsReserved(longAlias))
            {
                if (!IsValidAliasName(definition.CanonicalName))
                {
                    diagnostics.Add($"Parameter '{definition.CanonicalName}' cannot form an unambiguous fallback alias. Correct the template definition.");
                    continue;
                }

                longAlias = $"--param:{definition.CanonicalName}";
            }

            string? shortAlias = string.IsNullOrEmpty(definition.ShortName) ? null : $"-{definition.ShortName}";
            if (shortAlias is not null && IsReserved(shortAlias))
            {
                shortAlias = $"-p:{definition.ShortName}";
            }

            if (IsReserved(longAlias) || !aliases.Add(longAlias)
                || (shortAlias is not null && (IsReserved(shortAlias) || !aliases.Add(shortAlias))))
            {
                diagnostics.Add($"Parameter '{definition.CanonicalName}' has a reserved or duplicate effective alias. Correct the template aliases or command reservation.");
                continue;
            }

            TemplateParameterDefinition snapshot = definition with
            {
                Choices = Array.AsReadOnly<TemplateParameterChoice>([.. definition.Choices]),
            };
            parameters.Add(new(snapshot, longAlias, shortAlias));
        }

        return parameters;

        bool IsReserved(string alias) => reserved.Contains(alias) || reserved.Contains(alias.TrimStart('-'));
    }

    private static bool IsValidAliasName(string? name)
        => !string.IsNullOrEmpty(name) && !name.StartsWith('-')
            && !name.Any(character => char.IsWhiteSpace(character) || character is ':' or '=');

    private static bool IsSupportedDataType(string? dataType)
        => dataType?.ToLowerInvariant() is "string" or "text" or "choice" or "bool" or "boolean" or "int" or "integer" or "float" or "hex";

    private static bool IsValidValue(TemplateParameterDefinition definition, string value)
        => definition.DataType.ToLowerInvariant() switch
        {
            "string" or "text" => true,
            "choice" => definition.Choices.Any(choice => string.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase)),
            "bool" or "boolean" => bool.TryParse(value, out _),
            "int" or "integer" => long.TryParse(value, out _),
            "float" => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out _)
                || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            "hex" => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
            _ => false,
        };
}
