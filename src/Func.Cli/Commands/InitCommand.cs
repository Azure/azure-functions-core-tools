// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Initializes a new Azure Functions project. The actual scaffolding is
/// delegated to an <see cref="IProjectInitializer"/> contributed by a workload.
/// Each registered initializer may also contribute additional options to this
/// command (e.g. dotnet's <c>--target-framework</c>).
/// </summary>
internal class InitCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> StackOption { get; } = new("--stack", "-s")
    {
        Description = "The stack to use. Run `func workload list` to see what's installed."
    };

    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "The name of the function app project"
    };

    public Option<string?> LanguageOption { get; } = new("--language", "-l")
    {
        Description = "The programming language (e.g., C#, F#, JavaScript, TypeScript, Python)"
    };

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Force initialization even if the folder is not empty"
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadHintRenderer _hintRenderer;
    private readonly IReadOnlyList<IProjectInitializer> _initializers;

    public InitCommand(
        IInteractionService interaction,
        IWorkloadHintRenderer hintRenderer,
        IEnumerable<IProjectInitializer> initializers)
        : base("init", "Initialize a new Azure Functions project.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(hintRenderer);
        ArgumentNullException.ThrowIfNull(initializers);

        _interaction = interaction;
        _hintRenderer = hintRenderer;
        _initializers = initializers.ToList();

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
                // TODO: detect option-name collisions across workloads and surface
                // a workload-named error.
                Options.Add(option);
            }
        }
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        // Two workloads claiming the same stack is unrecoverable — `--stack`
        // would be ambiguous and auto-select would silently pick whichever
        // DI returned first. Validated lazily here (rather than in the ctor)
        // so an init-side conflict doesn't break unrelated commands like
        // `func start`.
        var dupes = _initializers
            .GroupBy(i => i.Stack, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dupes.Count > 0)
        {
            throw new InvalidOperationException(
                $"Two installed workloads both claim the '{string.Join("', '", dupes)}' stack. " +
                "Run `func workload list` to see them, then `func workload uninstall <name>` to remove one.");
        }

        ApplyPath(parseResult, createIfNotExists: true);

        var stack = parseResult.GetValue(StackOption);

        var initializer = await SelectInitializerAsync(stack, cancellationToken);
        if (initializer is null)
        {
            var installed = _initializers.Select(i => i.Stack).ToArray();
            var hint = installed.Length == 0
                ? new WorkloadHint(WorkloadHintKind.NoWorkloadsInstalled, "initialize a project", null, installed)
                : stack is not null
                    ? new WorkloadHint(WorkloadHintKind.NoMatchingStack, "initialize a project", stack, installed)
                    : new WorkloadHint(WorkloadHintKind.AmbiguousStackChoice, "initialize a project", null, installed);
            _hintRenderer.Render(hint);
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
            return _initializers.FirstOrDefault(i =>
                string.Equals(i.Stack, stack, StringComparison.OrdinalIgnoreCase));
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
