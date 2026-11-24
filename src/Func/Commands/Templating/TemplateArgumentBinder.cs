// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Commands.Templating;

/// <summary>
/// Binds command-line tokens to the native parameters of a selected template through the option names the template
/// authors in <c>func.host.json</c> <c>symbolInfo</c>.
/// </summary>
/// <remarks>
/// Binding only maps tokens to parameter names. Conversion, defaults, choices, and semantic validation remain owned
/// by the Template Engine.
/// </remarks>
internal static class TemplateArgumentBinder
{
    /// <summary>
    /// Binds <paramref name="tokens"/> to native parameter values of <paramref name="template"/>.
    /// </summary>
    /// <param name="template">The selected template.</param>
    /// <param name="tokens">Tokens left for the template, in command-line order.</param>
    /// <param name="reservedOptions">Options owned by the command that the template may not redefine.</param>
    /// <returns>Native parameter values keyed by parameter name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The template authors conflicting option names, or <paramref name="tokens"/> cannot be bound.
    /// </exception>
    public static IReadOnlyDictionary<string, string> Bind(
        FuncTemplateInfo template,
        IReadOnlyList<string> tokens,
        IReadOnlyList<Option> reservedOptions)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(reservedOptions);

        var command = new Command("template");
        Dictionary<string, ITemplateParameter> parametersByAlias = [with(StringComparer.OrdinalIgnoreCase)];
        Dictionary<Option, ITemplateParameter> parametersByOption = [];

        foreach ((string parameterName, FuncTemplateSymbol symbol) in template.HostData.SymbolInfo)
        {
            if (!template.ParameterDefinitions.TryGetValue(parameterName, out ITemplateParameter? parameter))
            {
                continue;
            }

            string? longName = ReadOptionName(template, parameterName, "longName", symbol.LongName);
            if (longName is null)
            {
                continue;
            }

            string? shortName = ReadOptionName(template, parameterName, "shortName", symbol.ShortName);
            string[] aliases = shortName is null ? [$"--{longName}"] : [$"--{longName}", $"-{shortName}"];
            foreach (string alias in aliases)
            {
                if (!parametersByAlias.TryAdd(alias, parameter))
                {
                    throw new InvalidOperationException(
                        $"Template '{template.Identity}' has conflicting func.host.json option '{alias}'.");
                }

                if (reservedOptions.Any(option => HasAlias(option, alias)))
                {
                    throw new InvalidOperationException(
                        $"Template '{template.Identity}' has func.host.json option '{alias}' that conflicts with a built-in option.");
                }
            }

            Option option = IsBoolean(parameter)
                ? new Option<bool>(aliases[0], aliases[1..])
                : new Option<string>(aliases[0], aliases[1..])
                {
                    Arity = string.IsNullOrEmpty(parameter.DefaultIfOptionWithoutValue)
                        ? ArgumentArity.ExactlyOne
                        : ArgumentArity.ZeroOrOne
                };
            command.Options.Add(option);
            parametersByOption.Add(option, parameter);
        }

        ParseResult parseResult = command.Parse(["template", .. tokens]);
        if (parseResult.UnmatchedTokens.Count > 0)
        {
            string token = tokens.First(value => parseResult.UnmatchedTokens.Contains(value));
            throw new InvalidOperationException(token.StartsWith('-')
                ? $"Unrecognized option '{token}'."
                : $"Unrecognized argument '{token}'. Specify the project path before template options.");
        }

        foreach (Option option in parametersByOption.Keys)
        {
            if (parseResult.GetResult(option) is { Implicit: false, IdentifierTokenCount: > 1 } repeated)
            {
                throw new InvalidOperationException(
                    $"Option '{repeated.IdentifierToken?.Value ?? option.Name}' was specified more than once.");
            }
        }

        if (parseResult.Errors.Count > 0)
        {
            throw new InvalidOperationException(parseResult.Errors[0].Message);
        }

        Dictionary<string, string> values = [with(StringComparer.Ordinal)];
        foreach ((Option option, ITemplateParameter parameter) in parametersByOption)
        {
            AddValue(values, parameter, parseResult.GetResult(option));
        }

        return values;
    }

    private static void AddValue(Dictionary<string, string> values, ITemplateParameter parameter, OptionResult? optionResult)
    {
        if (optionResult is null || optionResult.Implicit)
        {
            return;
        }

        if (optionResult.Tokens.Count > 0)
        {
            values[parameter.Name] = optionResult.Tokens[0].Value;
        }
        else if (!string.IsNullOrEmpty(parameter.DefaultIfOptionWithoutValue))
        {
            values[parameter.Name] = parameter.DefaultIfOptionWithoutValue;
        }
        else if (IsBoolean(parameter))
        {
            values[parameter.Name] = "true";
        }
        else
        {
            string alias = optionResult.IdentifierToken?.Value ?? optionResult.Option.Name;
            throw new InvalidOperationException($"Option '{alias}' requires a value.");
        }
    }

    private static string? ReadOptionName(FuncTemplateInfo template, string parameterName, string propertyName, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (!char.IsLetterOrDigit(name[0])
            || !name.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.'))
        {
            throw new InvalidOperationException(
                $"Template '{template.Identity}' has invalid func.host.json {propertyName} for symbol '{parameterName}'.");
        }

        return name;
    }

    private static bool IsBoolean(ITemplateParameter parameter)
        => string.Equals(parameter.DataType, "bool", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.DataType, "boolean", StringComparison.OrdinalIgnoreCase);

    private static bool HasAlias(Option option, string alias)
        => string.Equals(option.Name, alias, StringComparison.OrdinalIgnoreCase)
            || option.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase);
}
