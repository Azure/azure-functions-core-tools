// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

internal sealed class TemplateArgumentParser
{
    public TemplateArgumentParseResult Parse(
        IReadOnlyList<ResolvedTemplate> candidates,
        IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(tokens);

        List<TemplateArgumentParseResult> matches = [];
        List<TemplateArgumentDiagnostic> diagnostics = [];
        foreach (ResolvedTemplate candidate in candidates)
        {
            TemplateArgumentParseResult result = ParseCandidate(candidate, tokens);
            if (result.IsSuccess)
            {
                matches.Add(result);
            }
            else
            {
                diagnostics.AddRange(result.Diagnostics);
            }
        }

        if (matches.Count == 0)
        {
            TemplateArgumentDiagnostic diagnostic = BuildCombinedDiagnostic(candidates, tokens, diagnostics);
            return TemplateArgumentParseResult.Failure(diagnostic);
        }

        int highestPrecedence = matches.Max(match => match.Template!.Entry.Precedence);
        List<TemplateArgumentParseResult> preferred =
            [.. matches.Where(match => match.Template!.Entry.Precedence == highestPrecedence)];
        if (preferred.Count > 1)
        {
            string identities = string.Join(", ", preferred.Select(match => match.Template!.Entry.Identity).Order(StringComparer.Ordinal));
            return TemplateArgumentParseResult.Failure(
                new($"Multiple artifact templates match the project and arguments: {identities}."));
        }

        return preferred[0];
    }

    private static TemplateArgumentParseResult ParseCandidate(ResolvedTemplate candidate, IReadOnlyList<string> tokens)
    {
        Dictionary<string, TemplateParameterDefinition> parametersByAlias = new(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateParameterDefinition parameter in candidate.Parameters)
        {
            parametersByAlias.Add($"--{parameter.LongName}", parameter);
            if (parameter.ShortName is not null)
            {
                parametersByAlias.Add($"-{parameter.ShortName}", parameter);
            }
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            string alias = token;
            string? value = null;
            int equalsIndex = token.IndexOf('=');
            if (equalsIndex > 0)
            {
                alias = token[..equalsIndex];
                value = token[(equalsIndex + 1)..];
            }

            if (!parametersByAlias.TryGetValue(alias, out TemplateParameterDefinition? parameter))
            {
                return TemplateArgumentParseResult.Failure(new($"Unknown template argument '{alias}'."));
            }

            if (value is null)
            {
                if (++index >= tokens.Count || tokens[index].StartsWith("-", StringComparison.Ordinal))
                {
                    return TemplateArgumentParseResult.Failure(new($"Template argument '{alias}' requires a value."));
                }

                value = tokens[index];
            }

            if (parameter.Choices.Count > 0
                && !parameter.Choices.Any(choice => choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                return TemplateArgumentParseResult.Failure(
                    new($"The value '{value}' is not supported for '{alias}'.", parameter.LongName, value));
            }

            values[parameter.CanonicalName] = value;
        }

        foreach (TemplateParameterDefinition parameter in candidate.Parameters)
        {
            if (!values.ContainsKey(parameter.CanonicalName) && parameter.DefaultValue is not null)
            {
                values[parameter.CanonicalName] = parameter.DefaultValue;
            }
            else if (!values.ContainsKey(parameter.CanonicalName) && parameter.IsRequired)
            {
                return TemplateArgumentParseResult.Failure(
                    new($"Template argument '--{parameter.LongName}' is required.", parameter.LongName));
            }
        }

        return TemplateArgumentParseResult.Success(candidate, values);
    }

    private static TemplateArgumentDiagnostic BuildCombinedDiagnostic(
        IReadOnlyList<ResolvedTemplate> candidates,
        IReadOnlyList<string> tokens,
        IReadOnlyList<TemplateArgumentDiagnostic> diagnostics)
    {
        TemplateArgumentDiagnostic? invalidChoice = diagnostics.FirstOrDefault(diagnostic => diagnostic.Value is not null);
        if (invalidChoice is not null)
        {
            IReadOnlyList<string> choices = [.. candidates
                .SelectMany(candidate => candidate.Parameters)
                .Where(parameter => parameter.LongName.Equals(invalidChoice.ParameterName, StringComparison.OrdinalIgnoreCase))
                .SelectMany(parameter => parameter.Choices)
                .Select(choice => choice.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)];
            if (choices.Count > 0)
            {
                return new(
                    $"The value '{invalidChoice.Value}' is not supported for '--{invalidChoice.ParameterName}'. " +
                    $"Supported values are: {string.Join(", ", choices)}.");
            }
        }

        return diagnostics.FirstOrDefault()
            ?? new($"The template arguments are invalid: {string.Join(" ", tokens)}.");
    }
}

internal sealed record TemplateArgumentParseResult(
    ResolvedTemplate? Template,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<TemplateArgumentDiagnostic> Diagnostics)
{
    public bool IsSuccess => Template is not null;

    public static TemplateArgumentParseResult Success(
        ResolvedTemplate template,
        IReadOnlyDictionary<string, string> values) => new(template, values, []);

    public static TemplateArgumentParseResult Failure(TemplateArgumentDiagnostic diagnostic) =>
        new(null, new Dictionary<string, string>(StringComparer.Ordinal), [diagnostic]);
}

internal sealed record TemplateArgumentDiagnostic(string Message, string? ParameterName = null, string? Value = null);
