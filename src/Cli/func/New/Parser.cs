// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.Reflection;
using Azure.Functions.Cli.Abstractions;

namespace Azure.Functions.Cli
{
    /// <summary>
    /// Provides functionality for command-line parsing and execution
    /// in the Azure Functions CLI.
    /// </summary>
    public static class Parser
    {
        /// <summary>
        /// Represents the CLI option for displaying version information.
        /// </summary>
        public static readonly Option<bool> VersionOption = new("--version", ["-v"])
        {
            Description = "Show version information."
        };

        /// <summary>
        /// Represents an optional hidden argument for specifying a subcommand.
        /// </summary>
        public static readonly Argument<string> FuncSubCommand = new("subcommand") { Arity = ArgumentArity.ZeroOrOne, Hidden = true };

        /// <summary>
        /// Gets the root command for the Func CLI.
        /// </summary>
        /// <remarks>
        /// If you use this Command directly, you _must_ use <see cref="ParserConfiguration"/>
        /// and <see cref="InvocationConfiguration"/> to ensure that the command line parser
        /// and invoker are configured correctly.
        /// </remarks>
        public static RootCommand RootCommand { get; } = ConfigureCommandLine(new()
        {
            Description = "Azure Functions CLI",
            Directives = { new DiagramDirective(), new SuggestDirective(), new EnvironmentVariablesDirective() }
        });

        /// <summary>
        /// Gets or sets the parser configuration for the CLI.
        /// </summary>
        public static ParserConfiguration ParserConfiguration { get; set; } = new()
        {
            EnablePosixBundling = false
        };

        /// <summary>
        /// Gets represents the invocation configuration for the CLI.
        /// </summary>
        public static InvocationConfiguration InvocationConfiguration { get; } = new()
        {
            EnableDefaultExceptionHandler = false,
        };

        /// <summary>
        /// Configures the command-line interface, adding subcommands, options, and arguments.
        /// </summary>
        /// <param name="rootCommand">The root command to configure.</param>
        /// <returns>The configured <see cref="CliCommand"/>.</returns>
        private static RootCommand ConfigureCommandLine(RootCommand rootCommand)
        {
            for (int i = rootCommand.Options.Count - 1; i >= 0; i--)
            {
                Option option = rootCommand.Options[i];

                if (option is VersionOption)
                {
                    rootCommand.Options.RemoveAt(i); // why?
                }
                else if (option is HelpOption helpOption)
                {
                    helpOption.Action = new FuncHelpAction((HelpAction)helpOption.Action!);
                }
            }

            rootCommand.Options.Add(VersionOption);

            rootCommand.Arguments.Add(FuncSubCommand);

            rootCommand.SetAction(parseResult =>
            {
                if (parseResult.GetValue(VersionOption) && parseResult.Tokens.Count == 1)
                {
                    Spectre.Console.AnsiConsole.WriteLine($"Core Tools Version: {Common.Constants.CliVersion}");
                    return 0;
                }
                else
                {
                    parseResult.ShowHelp();
                    return 0;
                }
            });

            return rootCommand;
        }

        public static void AddSubcommand(Command subcommand)
        {
            RootCommand.Subcommands.Add(subcommand);
        }

        /// <summary>
        /// Retrieves a built-in command by name.
        /// </summary>
        /// <param name="commandName">The name of the command to retrieve.</param>
        /// <returns>The matching <see cref="Command"/>, or <c>null</c> if not found.</returns>
        public static Command GetBuiltInCommand(string commandName)
        {
            return RootCommand.Subcommands.FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// You probably want to use <see cref="Parse(string[])"/> instead of this method.
        /// This has to internally split the string into an array of arguments
        /// before parsing, which is not as efficient as using the array overload.
        /// And also won't always split tokens the way the user will expect on their shell.
        /// </summary>
        public static ParseResult Parse(string commandLineUnsplit) => RootCommand.Parse(commandLineUnsplit, ParserConfiguration);

        public static ParseResult Parse(string[] args) => RootCommand.Parse(args, ParserConfiguration);

        public static int Invoke(ParseResult parseResult) => parseResult.Invoke(InvocationConfiguration);

        public static Task<int> InvokeAsync(ParseResult parseResult, CancellationToken cancellationToken = default) => parseResult.InvokeAsync(InvocationConfiguration, cancellationToken);

        public static int Invoke(string[] args) => Invoke(Parse(args));

        public static Task<int> InvokeAsync(string[] args, CancellationToken cancellationToken = default) => InvokeAsync(Parse(args), cancellationToken);

        /// <summary>
        /// Handles exceptions that occur during command execution.
        /// </summary>
        /// <param name="exception">The exception that was thrown.</param>
        /// <param name="parseResult">The result of the command-line parsing.</param>
        /// <returns>An exit code indicating success or failure.</returns>
        internal static int ExceptionHandler(Exception exception, ParseResult parseResult)
        {
            if (exception is TargetInvocationException)
            {
                exception = exception.InnerException ?? exception;
            }

            if (exception is GracefulException)
            {
                WriteException(exception);
            }
            else if (exception is CommandParsingException)
            {
                WriteException(exception);
                parseResult.ShowHelp();
            }
            else
            {
                Spectre.Console.AnsiConsole.Write("Unhandled exception: ".Red().Bold());
                Spectre.Console.AnsiConsole.WriteLine(exception.ToString().Red().Bold());
            }

            return 1;
        }

        internal static void WriteException(Exception exception)
        {
            if (CommandLoggingContext.IsVerbose)
            {
                Spectre.Console.AnsiConsole.WriteException(exception);
            }
            else
            {
                Spectre.Console.AnsiConsole.WriteLine(exception.Message.Red().Bold());
            }
        }
    }
}
