// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Frozen;
using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Templates;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Binds the staged project-template journey without replacing the registered public initializer.
/// </summary>
internal sealed class InitExecutionCommand : FuncCliCommand
{
    private readonly IInitExecutionRunner _runner;
    private readonly InitExecutionRenderer _renderer;
    private readonly TemplateConstraintEvaluationSnapshot _availability;

    public InitExecutionCommand(IInitExecutionRunner runner, InitExecutionRenderer renderer, TemplateConstraintEvaluationSnapshot availability)
        : base("init", "Create or preview an installed project template in an empty directory.")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        Arguments.Add(TargetArgument);
        Options.Add(TemplateOption);
        Options.Add(NameOption);
        Options.Add(StackOption);
        Options.Add(LanguageOption);
        Options.Add(OutputOption);
        Options.Add(ForceOption);
        Options.Add(NonInteractiveOption);
        Options.Add(DryRunOption);
        TreatUnmatchedTokensAsErrors = false;
    }

    public Argument<string?> TargetArgument { get; } = new("path")
    {
        Description = "Empty target directory (defaults to the current directory); not a template reference.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Installed project-template identity or short name.",
        Arity = ArgumentArity.ExactlyOne,
    };

    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "Name passed separately to the selected project template.",
        Arity = ArgumentArity.ExactlyOne,
    };

    public Option<string?> StackOption { get; } = new("--stack", "-s")
    {
        Description = "Installed stack or runtime alias required for every generated Functions project.",
        Arity = ArgumentArity.ExactlyOne,
    };

    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "Installed language or alias required for every generated Functions project.",
        Arity = ArgumentArity.ExactlyOne,
    };

    public Option<string> OutputOption { get; } = new("--output")
    {
        Description = "Output mode (plain | json). Defaults to plain.",
        DefaultValueFactory = _ => "plain",
    };

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Not yet supported by this staged consumer; no content is deleted.",
    };

    public Option<bool> NonInteractiveOption { get; } = new("--non-interactive") { Description = "Fail instead of prompting for unresolved input." };

    public Option<bool> DryRunOption { get; } = new("--dry-run") { Description = "Preview native files and required CLI configuration without writing." };

    public ParseResult ParseInvocation(string[] tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        InitExecutionCommand invocation = new(_runner, _renderer, _availability);
        FuncRootCommand root = [invocation];
        return root.Parse([root.Name, Name, .. tokens], new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null,
        });
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        cancellationToken.ThrowIfCancellationRequested();
        if (parseResult.Configuration.EnablePosixBundling || parseResult.Configuration.ResponseFileTokenReplacer is not null)
        {
            throw new InvalidOperationException("The staged project command requires POSIX bundling and response-file expansion to be disabled.");
        }

        string? output = parseResult.GetValue(OutputOption);
        bool json = string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
        if (!json && !string.Equals(output, "plain", StringComparison.OrdinalIgnoreCase))
        {
            return _renderer.Render(InitExecutionResult.Reject("invalidOutput", "Use --output plain or --output json."), jsonOutput: false);
        }

        Token? positional = parseResult.GetResult(TargetArgument)?.Tokens.FirstOrDefault();
        if (positional is not null && (positional.Value.StartsWith('-') || positional.Value.StartsWith('@')
            || parseResult.Tokens.TakeWhile(token => !ReferenceEquals(token, positional)).Any(token => token.Type == TokenType.DoubleDash)))
        {
            positional = null;
        }

        WorkingDirectory directory;
        try
        {
            directory = positional is null ? WorkingDirectory.FromCwd() : WorkingDirectory.FromExplicit(positional.Value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return _renderer.Render(InitExecutionResult.Reject("invalidPath", $"Supply a valid target directory. {exception.Message}"), json);
        }

        HashSet<Token> consumed = new(ReferenceEqualityComparer.Instance);
        if (positional is not null)
        {
            consumed.Add(positional);
        }

        for (CommandResult? command = parseResult.CommandResult; command is not null; command = command.Parent as CommandResult)
        {
            foreach (OptionResult option in command.Children.OfType<OptionResult>())
            {
                consumed.UnionWith(option.Tokens);
            }
        }

        string[] raw = [.. parseResult.Tokens.Where(token => token.Type is not (TokenType.Command or TokenType.Option)
            && !consumed.Contains(token)).Select(token => token.Value)];
        InitExecutionRequest request = new(directory, parseResult.GetValue(TemplateOption), parseResult.GetValue(NameOption),
            parseResult.GetValue(StackOption), parseResult.GetValue(LanguageOption), parseResult.GetValue(ForceOption),
            parseResult.GetValue(NonInteractiveOption), parseResult.GetValue(DryRunOption), json, Array.AsReadOnly(raw), CollectReservedAliases());
        InitExecutionResult result = await _runner.ExecuteAsync(request, _availability, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return _renderer.Render(result, json);
    }

    private FrozenSet<string> CollectReservedAliases()
    {
        HashSet<string> aliases = new(StringComparer.Ordinal);
        for (Command? command = this; command is not null; command = command.Parents.OfType<Command>().FirstOrDefault())
        {
            foreach (Option option in command.Options.Where(option => ReferenceEquals(command, this) || option.Recursive))
            {
                aliases.Add(option.Name);
                aliases.UnionWith(option.Aliases);
            }
        }

        return aliases.ToFrozenSet(StringComparer.Ordinal);
    }
}
