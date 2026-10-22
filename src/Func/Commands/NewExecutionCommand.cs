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
/// Binds the staged native-item journey without registering a replacement for the public command.
/// </summary>
internal sealed class NewExecutionCommand : FuncCliCommand
{
    private readonly INewExecutionRunner _runner;
    private readonly NewExecutionRenderer _renderer;
    private readonly TemplateConstraintEvaluationSnapshot _availability;
    private readonly IReadOnlySet<string> _lifecycleAliases;

    public NewExecutionCommand(INewExecutionRunner runner, NewExecutionRenderer renderer, TemplateConstraintEvaluationSnapshot availability)
        : this(runner, renderer, availability, FrozenSet<string>.Empty)
    {
    }

    private NewExecutionCommand(INewExecutionRunner runner, NewExecutionRenderer renderer,
        TemplateConstraintEvaluationSnapshot availability, IReadOnlySet<string> lifecycleAliases)
        : base("new", "Create or preview an installed item template in an existing Functions project.")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _lifecycleAliases = lifecycleAliases;
        Arguments.Add(TemplateArgument);
        Options.Add(TemplateOption);
        Options.Add(PathOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(OutputOption);
        Options.Add(ForceOption);
        Options.Add(NonInteractiveOption);
        Options.Add(DryRunOption);
        Options.Add(ListOption);
        TreatUnmatchedTokensAsErrors = false;
        Validators.Add(result =>
        {
            if (GetPositionalReference(result) is not null && result.GetResult(TemplateOption) is not null)
            {
                result.AddError("Supply either <template> or --template/-t, not both.");
            }
        });
    }

    public Argument<string?> TemplateArgument { get; } = new("template")
    {
        Description = "Installed item identity or short name; use --template for reserved subcommand names.",
        Arity = ArgumentArity.ZeroOrOne,
    };

    public Option<string?> TemplateOption { get; } = new("--template", "-t")
    {
        Description = "Installed item identity or short name, including install, update and uninstall.",
        Arity = ArgumentArity.ExactlyOne,
    };

    public Option<WorkingDirectory> PathOption { get; } = new("--path")
    {
        Description = "Template execution directory (defaults to the current directory).",
        Arity = ArgumentArity.ExactlyOne,
        DefaultValueFactory = _ => WorkingDirectory.FromCwd(),
        CustomParser = ParsePath,
    };

    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "Name passed to the selected template.",
        Arity = ArgumentArity.ExactlyOne,
    };

    public Option<string?> LanguageOption { get; } = new("--language")
    {
        Description = "Supported project language or alias; overrides the project's language.",
        Arity = ArgumentArity.ExactlyOne,
    };

    public Option<string> OutputOption { get; } = new("--output")
    {
        Description = "Output mode (plain | json). Defaults to plain.",
        DefaultValueFactory = _ => "plain",
    };

    public Option<bool> ForceOption { get; } = new("--force") { Description = "Permit conflicting file changes." };

    public Option<bool> NonInteractiveOption { get; } = new("--non-interactive") { Description = "Fail instead of prompting for unresolved input." };

    public Option<bool> DryRunOption { get; } = new("--dry-run") { Description = "Preview file effects without applying them." };

    public Option<bool> ListOption { get; } = new("--list", "-l") { Description = "List eligible installed item templates." };

    /// <summary>
    /// Parses tokens after <c>new</c> in a fresh graph, routing lifecycle selectors before exposing the item-token tail.
    /// </summary>
    public ParseResult ParseInvocation(string[] tokens, IReadOnlyList<Func<Command>>? lifecycleFactories = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        Command[] lifecycle = [.. (lifecycleFactories ?? []).Select(create => create())];
        HashSet<string> reserved = new(StringComparer.Ordinal);
        foreach (Command child in lifecycle)
        {
            Reserve(child);
        }

        NewExecutionCommand invocation = new(_runner, _renderer, _availability, reserved.ToFrozenSet(StringComparer.Ordinal));
        FuncRootCommand root = [invocation];
        if (IsLifecycleInvocation(tokens, invocation, root, lifecycle))
        {
            foreach (Command child in lifecycle)
            {
                invocation.Subcommands.Add(child);
            }
        }

        return root.Parse([root.Name, Name, .. tokens], new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null,
        });

        void Reserve(Command command)
        {
            reserved.Add(command.Name);
            reserved.UnionWith(command.Aliases);
            foreach (Option option in command.Options)
            {
                reserved.Add(option.Name);
                reserved.UnionWith(option.Aliases);
            }

            foreach (Command child in command.Subcommands)
            {
                Reserve(child);
            }
        }
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        cancellationToken.ThrowIfCancellationRequested();
        if (parseResult.Configuration.EnablePosixBundling || parseResult.Configuration.ResponseFileTokenReplacer is not null)
        {
            throw new InvalidOperationException("The native-item command requires POSIX bundling and response-file expansion to be disabled.");
        }

        string? output = parseResult.GetValue(OutputOption);
        bool json = string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
        if (!json && !string.Equals(output, "plain", StringComparison.OrdinalIgnoreCase))
        {
            return _renderer.Render(NewExecutionResult.Reject("invalidOutput", "Use --output plain or --output json."), jsonOutput: false);
        }

        string? positional = GetPositionalReference(parseResult.CommandResult);
        Token? positionalToken = parseResult.GetResult(TemplateArgument)?.Tokens.FirstOrDefault();
        foreach (Token token in parseResult.Tokens)
        {
            if (ReferenceEquals(token, positionalToken))
            {
                break;
            }

            if (token.Type == TokenType.DoubleDash)
            {
                positional = null;
                break;
            }
        }

        string? reference = parseResult.GetValue(TemplateOption) ?? positional;
        HashSet<Token> consumed = new(ReferenceEqualityComparer.Instance);
        for (CommandResult? command = parseResult.CommandResult; command is not null; command = command.Parent as CommandResult)
        {
            foreach (OptionResult option in command.Children.OfType<OptionResult>())
            {
                consumed.UnionWith(option.Tokens);
            }
        }

        if (positional is not null)
        {
            consumed.UnionWith(parseResult.GetResult(TemplateArgument)!.Tokens);
        }

        // Unknown options can occupy the optional positional slot in Stage A. Keep them in the schema tail.
        string[] raw = [.. parseResult.Tokens.Where(token => token.Type is not (TokenType.Command or TokenType.Option)
            && !consumed.Contains(token)).Select(token => token.Value)];
        NewExecutionRequest request = new(
            parseResult.GetValue(PathOption)!,
            reference,
            parseResult.GetValue(NameOption),
            parseResult.GetValue(LanguageOption),
            parseResult.GetValue(ForceOption),
            parseResult.GetValue(NonInteractiveOption),
            parseResult.GetValue(DryRunOption),
            parseResult.GetValue(ListOption),
            json,
            Array.AsReadOnly(raw),
            CollectReservedAliases());
        NewExecutionResult result = await _runner.ExecuteAsync(request, _availability, cancellationToken);
        if (request.DryRun || !result.HasCreationOutcome)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return _renderer.Render(result, json);
    }

    private string? GetPositionalReference(CommandResult result)
    {
        string? value = result.GetResult(TemplateArgument)?.Tokens.FirstOrDefault()?.Value;
        return value is not null && !value.StartsWith('-') ? value : null;
    }

    private static WorkingDirectory ParsePath(ArgumentResult result)
    {
        try
        {
            return WorkingDirectory.FromExplicit(result.Tokens.Single().Value);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            result.AddError($"Invalid --path. Supply a directory inside a Functions project. {exception.Message}");
            return WorkingDirectory.FromCwd();
        }
    }

    private FrozenSet<string> CollectReservedAliases()
    {
        HashSet<string> aliases = new(_lifecycleAliases, StringComparer.Ordinal) { "install", "update", "uninstall" };
        AddSurface(this);
        for (Command? parent = Parents.OfType<Command>().FirstOrDefault(); parent is not null; parent = parent.Parents.OfType<Command>().FirstOrDefault())
        {
            foreach (Option option in parent.Options.Where(option => option.Recursive))
            {
                AddOption(option);
            }
        }

        return aliases.ToFrozenSet(StringComparer.Ordinal);

        void AddSurface(Command command)
        {
            foreach (Option option in command.Options)
            {
                AddOption(option);
            }

            foreach (Command child in command.Subcommands)
            {
                aliases.Add(child.Name);
                aliases.UnionWith(child.Aliases);
                AddSurface(child);
            }
        }

        void AddOption(Option option)
        {
            aliases.Add(option.Name);
            aliases.UnionWith(option.Aliases);
        }
    }

    private static bool IsLifecycleInvocation(string[] tokens, NewExecutionCommand command, FuncRootCommand root, IReadOnlyList<Command> lifecycle)
    {
        Option[] options = [.. command.Options, .. root.Options.Where(option => option.Recursive)];
        for (int index = 0; index < tokens.Length; index++)
        {
            string token = tokens[index];
            if (lifecycle.Any(child => child.Name == token || child.Aliases.Contains(token)))
            {
                return true;
            }

            Option? host = options.FirstOrDefault(option => Matches(option.Name) || option.Aliases.Any(Matches));
            if (host is null || ReferenceEquals(host, command.TemplateOption))
            {
                return false;
            }

            if (host.Arity.MaximumNumberOfValues > 0 && !token.Contains('=') && !token.Contains(':'))
            {
                if (host.Arity.MinimumNumberOfValues > 0)
                {
                    index += host.Arity.MinimumNumberOfValues;
                }
                else if (host is Option<bool> && index + 1 < tokens.Length && bool.TryParse(tokens[index + 1], out _))
                {
                    index++;
                }
            }

            bool Matches(string alias) => token == alias || token.StartsWith(alias + "=", StringComparison.Ordinal)
                || token.StartsWith(alias + ":", StringComparison.Ordinal);
        }

        return false;
    }
}
