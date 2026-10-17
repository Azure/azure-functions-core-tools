// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Commands;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;

namespace Azure.Functions.Cli;

internal sealed class NativeEntryArguments(IReadOnlyList<string> arguments)
{
    private readonly IReadOnlyList<string> _arguments = Array.AsReadOnly<string>(
        [.. arguments ?? throw new ArgumentNullException(nameof(arguments))]);

    public (IReadOnlyList<string> Tokens, int? PathIndex, WorkingDirectory Directory) Read(ParseResult parsed, FuncCliCommand command)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(command);
        Token identifier = parsed.CommandResult.IdentifierToken;
        int parsedIndex = 0;
        int rawIndex = 0;
        while (rawIndex < _arguments.Count && parsedIndex < parsed.Tokens.Count)
        {
            Token token = parsed.Tokens[parsedIndex];
            string raw = _arguments[rawIndex];
            if (ReferenceEquals(token, identifier))
            {
                if (raw != token.Value)
                {
                    throw new InvalidOperationException("The original command identifier could not be matched to the parsed graph.");
                }

                break;
            }

            if (raw == token.Value)
            {
                parsedIndex++;
            }
            else
            {
                int separator = raw.IndexOfAny(['=', ':']);
                if (separator < 1 || raw[..separator] != token.Value || token.Type != TokenType.Option
                    || parsedIndex + 1 >= parsed.Tokens.Count || raw[(separator + 1)..] != parsed.Tokens[parsedIndex + 1].Value)
                {
                    throw new InvalidOperationException("Original root tokens could not be accounted for without rewriting them.");
                }

                parsedIndex += 2;
            }

            rawIndex++;
        }

        if (rawIndex >= _arguments.Count || parsedIndex >= parsed.Tokens.Count)
        {
            throw new InvalidOperationException("The original invocation has no matching command boundary.");
        }

        string[] tail = [.. _arguments.Skip(rawIndex + 1)];
        List<Option> options = [.. command.Options];
        for (Command? parent = command.Parents.OfType<Command>().FirstOrDefault(); parent is not null;
            parent = parent.Parents.OfType<Command>().FirstOrDefault())
        {
            options.AddRange(parent.Options.Where(option => option.Recursive));
        }

        int? path = null;
        for (int index = 0; index < tail.Length; index++)
        {
            string value = tail[index];
            if (value == "--")
            {
                if (path is null && index + 2 == tail.Length)
                {
                    path = index + 1;
                }

                break;
            }

            if (value.StartsWith('-'))
            {
                int separator = value.IndexOf('=');
                if (separator < 0)
                {
                    separator = value.StartsWith("--param:", StringComparison.Ordinal) ? value.IndexOf(':', "--param:".Length)
                        : value.StartsWith("-p:", StringComparison.Ordinal) ? value.IndexOf(':', "-p:".Length) : value.IndexOf(':');
                }
                string alias = separator < 0 ? value : value[..separator];
                Option? option = options.SingleOrDefault(option => option.Name == alias || option.Aliases.Contains(alias));
                if (separator < 0 && index + 1 < tail.Length
                    && (option is null ? !TemplateArgumentParser.IsOptionLikeToken(tail[index + 1])
                        : option.Arity.MinimumNumberOfValues > 0 || (option is Option<bool> && bool.TryParse(tail[index + 1], out _))))
                {
                    index++;
                }

                continue;
            }

            if (!value.StartsWith('@') && path is null)
            {
                path = index;
            }
        }

        return (Array.AsReadOnly(tail), path, path is { } position ? WorkingDirectory.FromExplicit(tail[position]) : WorkingDirectory.FromCwd());
    }
}
