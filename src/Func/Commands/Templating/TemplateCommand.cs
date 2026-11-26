// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Azure.Functions.Cli.Commands.Templating;

/// <summary>
/// Base class for commands that accept options authored by an installed template.
/// </summary>
/// <remarks>
/// Template-authored options are only known after a template is selected, so unmatched tokens are not parse errors
/// for these commands. The command binds the remaining tokens against the selected template and rejects leftovers.
/// </remarks>
internal abstract class TemplateCommand : FuncCliCommand
{
    protected TemplateCommand(string name, string description)
        : base(name, description)
    {
        TreatUnmatchedTokensAsErrors = false;
    }

    /// <summary>
    /// Parser configuration for re-parsing tokens this command's parse already produced. Response files were expanded
    /// by that parse and are not expanded again.
    /// </summary>
    protected internal static ParserConfiguration TokenParserConfiguration { get; } = new()
    {
        EnablePosixBundling = false,
        ResponseFileTokenReplacer = null,
    };

    /// <summary>
    /// Returns, in command-line order, the tokens after this command that were not consumed by its options, its
    /// arguments, or inherited recursive options.
    /// </summary>
    /// <param name="parseResult">The parse result for this command.</param>
    /// <param name="passThroughOptions">
    /// Options whose tokens are returned as if they were unmatched, so the selected template owns them.
    /// </param>
    protected internal static IReadOnlyList<string> GetTemplateTokens(
        ParseResult parseResult,
        IReadOnlyCollection<Option> passThroughOptions)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(passThroughOptions);

        return [.. GetUnconsumedTokenIndexes(parseResult, passThroughOptions)
            .Select(index => parseResult.Tokens[index].Value)];
    }

    /// <summary>
    /// Returns a diagnostic when the first token the parse left unconsumed is a value System.CommandLine rejected for
    /// the Boolean option immediately before it, such as <c>--flag=value</c>; otherwise <see langword="null"/>.
    /// </summary>
    /// <param name="parseResult">The parse result for a command whose options include the Boolean option.</param>
    protected internal static string? GetInvalidBooleanValueError(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        // A Boolean option consumes the following argument token only when it parses as a Boolean, so a rejected
        // value is the first unconsumed token and directly follows the option's identifier token.
        int index = GetUnconsumedTokenIndexes(parseResult, []).FirstOrDefault(-1);
        if (index <= 0)
        {
            return null;
        }

        Token value = parseResult.Tokens[index];
        Token previous = parseResult.Tokens[index - 1];
        if (value.Type != TokenType.Argument || value.Value.StartsWith('-') || previous.Type != TokenType.Option)
        {
            return null;
        }

        bool isBooleanOption = parseResult.CommandResult.Command.Options.Any(option =>
            option.ValueType == typeof(bool)
            && (string.Equals(option.Name, previous.Value, StringComparison.Ordinal)
                || option.Aliases.Contains(previous.Value)));
        return isBooleanOption
            ? $"Invalid value '{value.Value}' for option '{previous.Value}'. Expected 'true' or 'false'."
            : null;
    }

    // Indexes into ParseResult.Tokens, in command-line order, of the tokens after this command that no option,
    // argument, or inherited recursive option consumed.
    private static IEnumerable<int> GetUnconsumedTokenIndexes(
        ParseResult parseResult,
        IReadOnlyCollection<Option> passThroughOptions)
    {
        HashSet<Token> consumed = [with(ReferenceEqualityComparer.Instance)];
        foreach (SymbolResult child in parseResult.CommandResult.Children)
        {
            if (child is OptionResult optionResult && !passThroughOptions.Contains(optionResult.Option))
            {
                if (optionResult.IdentifierToken is { } identifier)
                {
                    consumed.Add(identifier);
                }

                consumed.UnionWith(optionResult.Tokens);
            }
            else if (child is ArgumentResult argumentResult)
            {
                consumed.UnionWith(argumentResult.Tokens);
            }
        }

        HashSet<string> consumedOptionNames = [with(StringComparer.Ordinal)];
        foreach (OptionResult optionResult in parseResult.CommandResult.Children.OfType<OptionResult>())
        {
            if (!passThroughOptions.Contains(optionResult.Option))
            {
                consumedOptionNames.Add(optionResult.Option.Name);
                consumedOptionNames.UnionWith(optionResult.Option.Aliases);
            }
        }

        int commandTokenIndex = -1;
        for (int index = 0; index < parseResult.Tokens.Count; index++)
        {
            if (parseResult.Tokens[index].Type == TokenType.Command)
            {
                commandTokenIndex = index;
            }
        }

        // Repeated identifiers of a non-template option are recorded once as the identifier token, so later
        // occurrences are excluded by option name.
        return Enumerable.Range(commandTokenIndex + 1, parseResult.Tokens.Count - commandTokenIndex - 1)
            .Where(index => parseResult.Tokens[index] is var token
                && !consumed.Contains(token)
                && !(token.Type == TokenType.Option && consumedOptionNames.Contains(token.Value)));
    }

    /// <summary>
    /// Returns the options a template may not redefine: this command's options, excluding
    /// <paramref name="templateOwnedOptions"/>, and recursive options inherited from parent commands.
    /// </summary>
    protected IReadOnlyList<Option> GetReservedOptions(IReadOnlyCollection<Option> templateOwnedOptions)
    {
        ArgumentNullException.ThrowIfNull(templateOwnedOptions);

        List<Option> options = [.. Options.Where(option => !templateOwnedOptions.Contains(option))];
        for (Command? parent = Parents.OfType<Command>().FirstOrDefault(); parent is not null;
            parent = parent.Parents.OfType<Command>().FirstOrDefault())
        {
            options.AddRange(parent.Options.Where(option => option.Recursive));
        }

        return options;
    }
}
