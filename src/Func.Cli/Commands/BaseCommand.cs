// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Base class for all func CLI commands. Commands inherit from System.CommandLine.Command
/// and implement ExecuteAsync for their business logic. Follows the Aspire CLI pattern
/// where the command IS the System.CommandLine Command (no separate definition + handler).
///
/// Commands that operate on a project directory should call AddPathArgument() in their
/// constructor and ApplyPath(parseResult) in their execute method to support
/// 'func start [path]', 'func init [path]', etc.
/// </summary>
internal abstract class BaseCommand : Command
{
    /// <summary>
    /// Path argument for commands that operate on a project directory. Created
    /// per command instance so each command owns its own argument graph.
    /// </summary>
    protected Argument<string?>? PathArgument { get; private set; }

    protected BaseCommand(string name, string description) : base(name, description)
    {
        SetAction(async (parseResult, cancellationToken) =>
        {
            return await ExecuteAsync(parseResult, cancellationToken);
        });
    }

    /// <summary>
    /// Executes the command asynchronously with cancellation support. Parent-only
    /// commands that have subcommands but no execution logic of their own can
    /// inherit the default implementation, which invokes the help action wired
    /// to the root command's <see cref="HelpOption"/> (e.g. Spectre help). The
    /// help action itself decides which command to render help for based on
    /// <see cref="ParseResult.CommandResult"/>, so the user sees help for this
    /// command, not the root.
    /// </summary>
    protected virtual Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        // SCL 2.0.6 adds HelpOption to RootCommand, not to subcommands. Walk
        // from this command up through parents to find the nearest HelpOption.
        Command? current = this;
        while (current is not null)
        {
            var helpOption = current.Options.OfType<HelpOption>().FirstOrDefault();
            if (helpOption?.Action is SynchronousCommandLineAction sync)
            {
                return Task.FromResult(sync.Invoke(parseResult));
            }

            current = current.Parents.OfType<Command>().FirstOrDefault();
        }

        // Fallback: if we still can't find a help action, look at the root
        // result. In practice this is the same lookup, but it's defensive
        // against constructed-but-unparented commands.
        var rootHelp = parseResult.RootCommandResult.Command.Options.OfType<HelpOption>().FirstOrDefault();
        if (rootHelp?.Action is SynchronousCommandLineAction rootSync)
        {
            return Task.FromResult(rootSync.Invoke(parseResult));
        }

        // Help action not wired (command was constructed outside Parser.CreateCommand,
        // or there is no HelpOption). Nothing meaningful to render — return success.
        return Task.FromResult(0);
    }

    /// <summary>
    /// Adds the optional [path] argument to this command. Call from the constructor
    /// of commands that operate on a project directory.
    /// </summary>
    protected void AddPathArgument()
    {
        PathArgument = new("path")
        {
            Description = "The project directory to use (defaults to current directory)",
            Arity = ArgumentArity.ZeroOrOne,
            CustomParser = result =>
            {
                var token = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
                if (token is not null && token.StartsWith('-'))
                {
                    result.AddError($"Unrecognized option '{token}'.");
                    return null;
                }
                return token;
            }
        };
        Arguments.Add(PathArgument);
    }

    /// <summary>
    /// If [path] was specified, validates it and changes the working directory.
    /// Call at the start of ExecuteAsync in commands that use AddPathArgument().
    /// When <paramref name="createIfNotExists"/> is true, the directory is created
    /// if it does not exist (useful for init/new).
    /// </summary>
    protected void ApplyPath(ParseResult parseResult, bool createIfNotExists = false)
    {
        if (PathArgument is null)
        {
            return;
        }

        var path = parseResult.GetValue(PathArgument);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            if (createIfNotExists)
            {
                Directory.CreateDirectory(fullPath);
            }
            else
            {
                throw new GracefulException(
                    $"The specified path does not exist: '{path}'",
                    isUserError: true);
            }
        }

        Directory.SetCurrentDirectory(fullPath);
    }
}
