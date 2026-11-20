// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Binds template-specific command-line options, named by the template's <c>func.host.json</c> symbol info, to template parameters.
/// </summary>
internal class TemplateParameterBinder
{
    private const string TemplateCommandName = "template";

    // The command parse has already expanded response files.
    private static readonly ParserConfiguration _parserConfiguration = new() { ResponseFileTokenReplacer = null };

    /// <summary>
    /// Parses <paramref name="templateArguments"/> against the options that <paramref name="template"/> declares.
    /// </summary>
    /// <param name="template">The template whose parameters receive the values.</param>
    /// <param name="parseResult">The command's parse result, used to read declared options that a template option maps to.</param>
    /// <param name="templateArguments">The tokens the command did not consume, in their original order.</param>
    /// <param name="bindableOptions">Declared command options that template options may map to.</param>
    /// <returns>The explicitly supplied values, keyed by template parameter name.</returns>
    /// <exception cref="InvalidOperationException">
    /// The arguments are not valid for the template, or the template's <c>func.host.json</c> is not valid.
    /// </exception>
    public virtual IReadOnlyDictionary<string, string> Bind(
        FuncTemplateInfo template,
        ParseResult parseResult,
        IReadOnlyList<string> templateArguments,
        IReadOnlyCollection<Option> bindableOptions)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(templateArguments);
        ArgumentNullException.ThrowIfNull(bindableOptions);

        List<Option> declaredOptions = GetDeclaredOptions(parseResult.CommandResult.Command);
        Command command = new(TemplateCommandName);
        List<(ITemplateParameter Parameter, Option Option)> templateOptions = [];
        Dictionary<Option, string> declaredClaims = [];
        Dictionary<string, string> values = [with(StringComparer.Ordinal)];

        foreach ((string parameterName, FuncTemplateSymbol symbol) in template.HostData.SymbolInfo)
        {
            string? longName = ReadName(template, parameterName, "longName", symbol.LongName);
            if (longName is null)
            {
                continue;
            }

            string? shortName = ReadName(template, parameterName, "shortName", symbol.ShortName);

            if (!template.ParameterDefinitions.TryGetValue(parameterName, out ITemplateParameter? parameter))
            {
                throw InvalidHostData(template, $"'{parameterName}' is not a template parameter.");
            }

            string[] aliases = shortName is null ? [$"--{longName}"] : [$"--{longName}", $"-{shortName}"];
            Option? declared = declaredOptions.FirstOrDefault(option => aliases.Any(alias => HasAlias(option, alias)));
            if (declared is not null)
            {
                // A template option may only rename a stack-owned option in full; it must not reinterpret built-ins.
                if (!bindableOptions.Contains(declared) || !aliases.All(alias => HasAlias(declared, alias)))
                {
                    throw InvalidHostData(template, $"option '{aliases[0]}' conflicts with the '{declared.Name}' command option.");
                }

                if (!declaredClaims.TryAdd(declared, aliases[0]))
                {
                    throw InvalidHostData(template, $"option '{aliases[0]}' conflicts with the template option '{declaredClaims[declared]}'.");
                }

                AddValue(values, parameter, parseResult.GetResult(declared));
                continue;
            }

            if (command.Options.FirstOrDefault(option => aliases.Any(alias => HasAlias(option, alias))) is { } duplicate)
            {
                throw InvalidHostData(template, $"option '{aliases[0]}' conflicts with the template option '{duplicate.Name}'.");
            }

            Option templateOption = IsBool(parameter)
                ? new Option<bool>(aliases[0], aliases[1..])
                : new Option<string>(aliases[0], aliases[1..])
                {
                    Arity = string.IsNullOrEmpty(parameter.DefaultIfOptionWithoutValue) ? ArgumentArity.ExactlyOne : ArgumentArity.ZeroOrOne
                };
            command.Options.Add(templateOption);
            templateOptions.Add((parameter, templateOption));
        }

        foreach (Option option in bindableOptions)
        {
            if (!declaredClaims.ContainsKey(option) && parseResult.GetResult(option) is { Implicit: false } explicitOption)
            {
                string alias = explicitOption.IdentifierToken?.Value ?? option.Name;
                throw new InvalidOperationException($"Option '{alias}' is not supported by template '{template.Identity}'.");
            }
        }

        // Prefixing the command name keeps a template argument that equals it from being read as the command.
        ParseResult result = command.Parse([TemplateCommandName, .. templateArguments], _parserConfiguration);
        if (result.UnmatchedTokens.Count > 0)
        {
            string token = result.UnmatchedTokens[0];
            throw new InvalidOperationException(token.StartsWith('-')
                ? $"Unrecognized option '{token}'."
                : $"Unrecognized argument '{token}'. Specify the project path before template options.");
        }

        if (result.Errors.Count > 0)
        {
            throw new InvalidOperationException(result.Errors[0].Message);
        }

        foreach ((ITemplateParameter parameter, Option templateOption) in templateOptions)
        {
            AddValue(values, parameter, result.GetResult(templateOption));
        }

        return values;
    }

    private static void AddValue(Dictionary<string, string> values, ITemplateParameter parameter, OptionResult? result)
    {
        if (result is null || result.Implicit)
        {
            return;
        }

        string alias = result.IdentifierToken?.Value ?? result.Option.Name;
        if (result.IdentifierTokenCount > 1)
        {
            throw new InvalidOperationException($"Option '{alias}' was specified more than once.");
        }

        if (result.Tokens.Count > 0)
        {
            string value = result.Tokens[0].Value;
            if (string.Equals(parameter.DataType, "choice", StringComparison.OrdinalIgnoreCase)
                && parameter.Choices is { Count: > 0 } choices
                && !choices.ContainsKey(value))
            {
                string validValues = string.Join(", ", choices.Keys);
                throw new InvalidOperationException(
                    $"Invalid value '{value}' for option '{alias}'. Valid values: {validValues}.");
            }

            values[parameter.Name] = value;
        }
        else if (!string.IsNullOrEmpty(parameter.DefaultIfOptionWithoutValue))
        {
            values[parameter.Name] = parameter.DefaultIfOptionWithoutValue;
        }
        else if (IsBool(parameter))
        {
            values[parameter.Name] = "true";
        }
        else
        {
            throw new InvalidOperationException($"Option '{alias}' requires a value.");
        }
    }

    private static string? ReadName(FuncTemplateInfo template, string parameterName, string propertyName, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // Restricting names keeps them from being read as values, delimiters or another option by the parser.
        if (!char.IsLetterOrDigit(name[0]) || !name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw InvalidHostData(
                template,
                $"'{propertyName}' of symbol '{parameterName}' must start with a letter or digit and contain only letters, digits, '-', '_' or '.'.");
        }

        return name;
    }

    private static List<Option> GetDeclaredOptions(Command command)
    {
        List<Option> options = [.. command.Options];
        for (Command? parent = command.Parents.OfType<Command>().FirstOrDefault(); parent is not null;
            parent = parent.Parents.OfType<Command>().FirstOrDefault())
        {
            options.AddRange(parent.Options.Where(option => option.Recursive));
        }

        return options;
    }

    private static bool HasAlias(Option option, string alias)
        => string.Equals(option.Name, alias, StringComparison.Ordinal) || option.Aliases.Contains(alias);

    private static bool IsBool(ITemplateParameter parameter)
        => string.Equals(parameter.DataType, "bool", StringComparison.OrdinalIgnoreCase)
            || string.Equals(parameter.DataType, "boolean", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException InvalidHostData(FuncTemplateInfo template, string reason)
        => new($"Template '{template.Identity}' has an invalid func.host.json: {reason}");
}
