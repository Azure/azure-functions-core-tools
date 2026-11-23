// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Hidden argument that follows <see cref="PathArgument"/> and keeps the tokens a command does not declare,
/// so they can be parsed against the selected template's options.
/// </summary>
internal sealed class TemplateArgumentsArgument : Argument<string[]>
{
    public TemplateArgumentsArgument() : base("template-arguments")
    {
        Description = "Options for the selected template.";
        Arity = ArgumentArity.ZeroOrMore;
        Hidden = true;
    }

    /// <summary>
    /// Returns the captured tokens in their original order, keeping a <c>--</c> delimiter that precedes any of them.
    /// </summary>
    /// <remarks>
    /// Tokens released by <see cref="PathArgument"/> are appended after the others, so the order comes from the full token list.
    /// </remarks>
    public IReadOnlyList<string> GetTokens(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        if (parseResult.GetResult(this) is not { Tokens.Count: > 0 } result)
        {
            return [];
        }

        HashSet<Token> captured = new(result.Tokens, ReferenceEqualityComparer.Instance);
        List<string> tokens = [];
        bool delimiterSeen = false;
        bool delimiterAdded = false;
        foreach (Token token in parseResult.Tokens)
        {
            if (token.Type == TokenType.DoubleDash)
            {
                delimiterSeen = true;
            }
            else if (captured.Contains(token))
            {
                if (delimiterSeen && !delimiterAdded)
                {
                    tokens.Add("--");
                    delimiterAdded = true;
                }

                tokens.Add(token.Value);
            }
        }

        return tokens;
    }
}
