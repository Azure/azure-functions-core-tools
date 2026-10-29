// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace Azure.Functions.Cli.Templates.Selection;

/// <summary>
/// Retains template tokens without collapsing duplicate options or confusing equal-valued host and template arguments.
/// </summary>
internal static class TemplateArgumentTransport
{
    public static (ImmutableArray<string> Arguments, ImmutableArray<string> Reserved, ImmutableArray<string> Repeated) Capture(
        ParseResult result, Argument path, IEnumerable<Option> builtIns)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(builtIns);
        List<Option> options = [.. builtIns];
        for (var parent = result.CommandResult.Parent as CommandResult; parent is not null; parent = parent.Parent as CommandResult)
        {
            options.AddRange(parent.Command.Options.Where(option => option.Recursive));
        }

        Dictionary<string, string> aliases = new(StringComparer.Ordinal);
        HashSet<Token> consumed = new(ReferenceEqualityComparer.Instance);
        foreach (Option option in options)
        {
            aliases[option.Name] = option.Name;
            foreach (string alias in option.Aliases)
            {
                aliases[alias] = option.Name;
            }

            if (result.GetResult(option) is { } bound)
            {
                consumed.UnionWith(bound.Tokens);
            }
        }

        if (result.GetResult(path) is { } target)
        {
            consumed.UnionWith(target.Tokens);
        }

        Token[] tokens = [.. result.Tokens];
        ImmutableArray<string> arguments = [.. tokens.Where(token => token.Type is not TokenType.Command and not TokenType.DoubleDash
            && !consumed.Contains(token) && !(token.Type == TokenType.Option && aliases.ContainsKey(token.Value))).Select(token => token.Value)];
        ImmutableArray<string> repeated = [.. tokens.Where(token => token.Type == TokenType.Option && aliases.ContainsKey(token.Value))
            .GroupBy(token => aliases[token.Value], StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key)];
        return (arguments, [.. aliases.Keys], repeated);
    }
}
