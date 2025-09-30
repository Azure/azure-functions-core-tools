// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Abstractions;
using static Azure.Functions.Cli.Parser;
using CommandResult = System.CommandLine.Parsing.CommandResult;

namespace Azure.Functions.Cli
{
    /// <summary>
    /// Provides extension methods for working with <see cref="ParseResult"/> instances.
    /// </summary>
    public static class ParseResultExtensions
    {
        /// <summary>
        /// Retrieves the root subcommand result from the parsed command-line arguments.
        /// </summary>
        /// <param name="parseResult">The <see cref="ParseResult"/> instance to extract the subcommand from.</param>
        /// <returns>The name of the root subcommand, or an empty string if none is found.</returns>
        public static string RootSubCommandResult(this ParseResult parseResult)
        {
            return parseResult.RootCommandResult.Children?
                .Select(child => GetSymbolResultValue(parseResult, child))
                .FirstOrDefault(subcommand => !string.IsNullOrEmpty(subcommand)) ?? string.Empty;
        }

        // /// <summary>
        // /// Determines whether the parsed command is a built-in Azure Functions CLI command.
        // /// </summary>
        // /// <param name="parseResult">The <see cref="ParseResult"/> instance to check.</param>
        // /// <returns><c>true</c> if the command is a built-in Azure Functions CLI command; otherwise, <c>false</c>.</returns>
        // public static bool IsFuncBuiltInCommand(this ParseResult parseResult)
        // {
        //     return string.IsNullOrEmpty(parseResult.RootSubCommandResult()) ||
        //         GetBuiltInCommand(parseResult.RootSubCommandResult()) is not null;
        // }

        // /// <summary>
        // /// Determines whether the parsed command is a top-level Azure Functions CLI command.
        // /// </summary>
        // /// <param name="parseResult">The <see cref="ParseResult"/> instance to check.</param>
        // /// <returns><c>true</c> if the command is a top-level command; otherwise, <c>false</c>.</returns>
        // public static bool IsTopLevelFuncCommand(this ParseResult parseResult)
        // {
        //     return parseResult.CommandResult.Command.Equals(Parser.RootCommand)
        //         && string.IsNullOrEmpty(parseResult.RootSubCommandResult());
        // }

        // /// <summary>
        // /// Determines whether the parsed command can be invoked based on its structure.
        // /// </summary>
        // /// <param name="parseResult">The <see cref="ParseResult"/> instance to check.</param>
        // /// <returns><c>true</c> if the command can be invoked; otherwise, <c>false</c>.</returns>
        // public static bool CanBeInvoked(this ParseResult parseResult)
        // {
        //     return Parser.GetBuiltInCommand(parseResult.RootSubCommandResult()) is not null ||
        //         parseResult.Tokens.Any(token => token.Type == CliTokenType.Directive) ||
        //         (parseResult.IsTopLevelFuncCommand() && string.IsNullOrEmpty(parseResult.GetValue(FuncSubCommand)));
        // }

        /// <summary>
        /// Extracts sub-arguments from a given set of command-line arguments.
        /// </summary>
        /// <param name="args">The original command-line arguments.</param>
        /// <returns>An array of sub-arguments.</returns>
        public static string[] GetSubArguments(this string[] args)
        {
            var subargs = args.ToList();

            return subargs
                .SkipWhile(arg => arg.Equals("func"))
                .Skip(1) // remove top level command (e.g. start or new)
                .ToArray();
        }

        /// <summary>
        /// Retrieves the value of a given symbol result from the parse result.
        /// </summary>
        /// <param name="parseResult">The <see cref="ParseResult"/> instance to inspect.</param>
        /// <param name="symbolResult">The symbol result to extract the value from.</param>
        /// <returns>The extracted symbol value, or <c>null</c> if not found.</returns>
        private static string GetSymbolResultValue(ParseResult parseResult, SymbolResult symbolResult) => symbolResult switch
        {
            CommandResult commandResult => commandResult.Command.Name,
            ArgumentResult argResult => argResult.Tokens.FirstOrDefault()?.Value ?? string.Empty,
            _ => parseResult.GetResult(FuncSubCommand)?.GetValueOrDefault<string>()
        };

        public static int HandleMissingCommand(this ParseResult parseResult)
        {
            Spectre.Console.AnsiConsole.WriteLine("Required command was not provided.".Red());
            parseResult.ShowHelp();
            return 1;
        }

        public static void ShowHelp(this ParseResult parseResult)
        {
            // take from the start of the list until we hit an option/--/unparsed token
            // since commands can have arguments, we must take those as well in order to get accurate help
            Parse([
                ..parseResult.Tokens.TakeWhile(token => token.Type == TokenType.Argument || token.Type == TokenType.Command || token.Type == TokenType.Directive).Select(t => t.Value),
                "-h"
            ]).Invoke();
        }

        public static void ShowHelpOrErrorIfAppropriate(this ParseResult parseResult)
        {
            if (parseResult.Errors.Any())
            {
                var unrecognizedTokenErrors = parseResult.Errors.Where(error =>
                {
                    // Can't really cache this access in a static because it implicitly depends on the environment.
                    var rawResourcePartsForThisLocale = DistinctFormatStringParts("Unrecognized command or argument '{0}'");
                    return ErrorContainsAllParts(error.Message, rawResourcePartsForThisLocale);
                });
                if (parseResult.CommandResult.Command.TreatUnmatchedTokensAsErrors ||
                    parseResult.Errors.Except(unrecognizedTokenErrors).Any())
                {
                    throw new CommandParsingException(
                        message: string.Join(Environment.NewLine, parseResult.Errors.Select(e => e.Message)),
                        parseResult: parseResult);
                }
            }

            /// <summary>Splits a .NET format string by the format placeholders (the {N} parts) to get an array of the literal parts, to be used in message-checking</summary>
            static string[] DistinctFormatStringParts(string formatString)
            {
                return Regex.Split(formatString, @"{[0-9]+}"); // match the literal '{', followed by any of 0-9 one or more times, followed by the literal '}'
            }

            /// <summary>given a string and a series of parts, ensures that all parts are present in the string in sequential order</summary>
            static bool ErrorContainsAllParts(ReadOnlySpan<char> error, string[] parts)
            {
                foreach (var part in parts)
                {
                    var foundIndex = error.IndexOf(part);
                    if (foundIndex != -1)
                    {
                        error = error.Slice(foundIndex + part.Length);
                        continue;
                    }
                    else
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }
}
