// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Initializes a new Azure Functions project. The actual scaffolding is
/// delegated to an <see cref="IProjectInitializer"/> contributed by a workload.
/// Each registered initializer may also contribute additional options to this
/// command (e.g. dotnet's <c>--target-framework</c>).
/// </summary>
internal class InitCommand : BaseCommand
{
    public static readonly Option<string?> StackOption = new("--stack", "-s")
    {
        Description = "The stack to use. Run `func workload list` to see what's installed."
    };

    public static readonly Option<string?> NameOption = new("--name", "-n")
    {
        Description = "The name of the function app project"
    };

    public static readonly Option<string?> LanguageOption = new("--language", "-l")
    {
        Description = "The programming language (e.g., C#, F#, JavaScript, TypeScript, Python)"
    };

    public static readonly Option<bool> ForceOption = new("--force")
    {
        Description = "Force initialization even if the folder is not empty"
    };

    private readonly IInteractionService _interaction;
    private readonly IReadOnlyList<IProjectInitializer> _initializers;

    public InitCommand(
        IInteractionService interaction,
        IEnumerable<IProjectInitializer> initializers)
        : base("init", "Initialize a new Azure Functions project.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(initializers);

        _interaction = interaction;
        _initializers = initializers.ToList();

        // Two workloads claiming the same stack is unrecoverable — `--stack`
        // would be ambiguous and auto-select would silently pick whichever DI
        // returned first. Fail fast at startup with a clear message instead.
        var dupes = _initializers
            .GroupBy(i => i.Stack, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Multiple workloads register an IProjectInitializer for the same stack: {string.Join(", ", dupes)}. " +
                "Each stack must be owned by exactly one workload.");
        }

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        // Workload-contributed options are attached after built-ins so they
        // appear as a clearly-grouped block in --help output.
        foreach (var initializer in _initializers)
        {
            foreach (var option in initializer.GetInitOptions())
            {
                Options.Add(option);
            }
        }
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        ApplyPath(parseResult, createIfNotExists: true);

        var stack = parseResult.GetValue(StackOption);

        var initializer = await SelectInitializerAsync(stack, cancellationToken);
        if (initializer is null)
        {
            var installed = _initializers.Select(i => i.Stack).ToArray();
            WorkloadHints.WriteNoMatchingWorkload(
                _interaction,
                installed,
                actionDescription: "initialize a project",
                requestedStack: stack);
            return 1;
        }

        var context = new InitContext(
            ProjectPath: Directory.GetCurrentDirectory(),
            ProjectName: parseResult.GetValue(NameOption),
            Language: parseResult.GetValue(LanguageOption),
            Force: parseResult.GetValue(ForceOption));

        await initializer.InitializeAsync(context, parseResult, cancellationToken);

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Project initialized for ")
            .Code(initializer.Stack)
            .Muted("."));

        return 0;
    }

    private async Task<IProjectInitializer?> SelectInitializerAsync(string? stack, CancellationToken cancellationToken)
    {
        if (_initializers.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(stack))
        {
            return _initializers.FirstOrDefault(i => i.CanHandle(stack));
        }

        // No stack specified — auto-select if there's only one initializer
        // installed (the common case for engineers using a single language).
        if (_initializers.Count == 1)
        {
            return _initializers[0];
        }

        // Multiple initializers — prompt the user to pick. In non-interactive
        // mode we fall back to the "no match" error so scripts get a clear
        // failure instead of silently picking the first option.
        if (!_interaction.IsInteractive)
        {
            return null;
        }

        var choices = _initializers.Select(i => i.Stack).ToList();
        var picked = await _interaction.PromptForSelectionAsync(
            "Select a stack:",
            choices,
            cancellationToken);

        return _initializers.FirstOrDefault(i => i.Stack == picked);
    }
}
