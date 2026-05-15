// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
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
        Description = "The name of the Function App project"
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

        LanguageOption.Description = BuildLanguageOptionDescription(_initializers);

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        // Workload-contributed options are attached after built-ins so they
        // appear as a clearly-grouped block in --help output.
        foreach (IProjectInitializer initializer in _initializers)
        {
            foreach (Option option in initializer.GetInitOptions())
            {
                // TODO: detect option-name collisions across workloads and surface
                // a workload-named error.
                Options.Add(option);
            }
        }
    }

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        // Two workloads claiming the same stack is unrecoverable: `--stack`
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

        WorkingDirectory workingDirectory = parseResult.GetValue(PathArgument!)!;
        workingDirectory.CreateIfNotExists();
        bool force = parseResult.GetValue(ForceOption);
        string? language = parseResult.GetValue(LanguageOption);
        if (!string.IsNullOrWhiteSpace(language))
        {
            language = language.Trim().ToLowerInvariant();
        }
        string? requestedStack = parseResult.GetValue(StackOption);

        // Refuse to overwrite an existing Functions project. Either host.json
        // or .func/config.json being present is enough to count: both are
        // host-owned skeleton files we'd otherwise rewrite. --force opts in
        // to a re-init.
        if (!force && IsAlreadyInitialized(workingDirectory.Info, out string existingFile))
        {
            _interaction.WriteError(
                $"This directory already contains a Functions project ('{existingFile}' is present). " +
                "Pass --force to re-initialize.");
            return 1;
        }

        InitializerSelection selection = await SelectInitializerAsync(requestedStack, cancellationToken);

        if (selection.Initializer is not null)
        {
            (string? resolved, int? errorCode) = await ResolveLanguageAsync(language, selection.Initializer, cancellationToken);
            if (errorCode is int code)
            {
                return code;
            }

            language = resolved;
        }

        // Always lay down the host-owned skeleton so a folder is usable even
        // when no initializer runs. Workload initializers layer their files
        // on top after this point.
        WriteHostJson(workingDirectory.Info, force);
        WriteFuncProjectConfig(workingDirectory.Info, selection.Initializer?.Stack, language, force);
        _interaction.WriteBlankLine();

        if (selection.Initializer is null)
        {
            _hintRenderer.Render(selection.Hint!);
            return 0;
        }

        if (selection.Hint is not null)
        {
            // Auto-select info line, rendered before initializer output so
            // the user sees why this stack was chosen.
            _hintRenderer.Render(selection.Hint);
        }

        var context = new InitContext(
            WorkingDirectory: workingDirectory,
            ProjectName: parseResult.GetValue(NameOption),
            Language: language,
            Force: force);

        await selection.Initializer.InitializeAsync(context, parseResult, cancellationToken);

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Project initialized for ")
            .Code(selection.Initializer.Stack)
            .Muted("."));

        return 0;
    }

    // Builds the help text for `--language`. Languages are pulled from
    // installed initializers' SupportedLanguages, lowercased and sorted for
    // a stable, consistent presentation. The `--language` value itself is
    // normalized to lower case in ExecuteAsync, so matching is case-insensitive.
    private static string BuildLanguageOptionDescription(IReadOnlyList<IProjectInitializer> initializers)
    {
        var languages = initializers
            .SelectMany(i => i.SupportedLanguages)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();

        return languages.Count == 0
            ? "The programming language. Install a stack workload (`func workload install <id>`) to see supported values."
            : "The programming language. Supported values: " + string.Join(", ", languages) + ".";
    }

    private static bool IsAlreadyInitialized(DirectoryInfo workingDirectory, out string existingFile)
    {
        string hostJson = Path.Combine(workingDirectory.FullName, "host.json");
        if (File.Exists(hostJson))
        {
            existingFile = "host.json";
            return true;
        }

        string config = Path.Combine(workingDirectory.FullName, ".func", "config.json");
        if (File.Exists(config))
        {
            existingFile = Path.Combine(".func", "config.json");
            return true;
        }

        existingFile = string.Empty;
        return false;
    }

    private void WriteHostJson(DirectoryInfo workingDirectory, bool force)
    {
        string path = Path.Combine(workingDirectory.FullName, "host.json");

        // Treat an existing file as user-owned unless --force was passed.
        // Workloads that need extensionBundle / logging defaults will merge
        // into the file we just wrote (or the user's existing one).
        if (File.Exists(path) && !force)
        {
            return;
        }

        try
        {
            const string MinimalHostJson = "{\n  \"version\": \"2.0\"\n}\n";
            File.WriteAllText(path, MinimalHostJson);

            _interaction.WriteLine(l => l
                .Muted("· Wrote ")
                .Code("host.json")
                .Muted("."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _interaction.WriteLine(l => l
                .Warning("! ")
                .Muted("Could not write host.json (")
                .Code(ex.Message)
                .Muted(")."));
        }
    }

    private void WriteFuncProjectConfig(DirectoryInfo workingDirectory, string? stack, string? language, bool force)
    {
        string folder = Path.Combine(workingDirectory.FullName, ".func");
        string path = Path.Combine(folder, "config.json");

        // Treat an existing file as user-owned unless --force was passed.
        if (File.Exists(path) && !force)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);

            var payload = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(stack))
            {
                payload["stack"] = stack;
            }
            if (!string.IsNullOrWhiteSpace(language))
            {
                payload["language"] = language;
            }

            string json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            });
            File.WriteAllText(path, json + Environment.NewLine);

            _interaction.WriteLine(l => l
                .Muted("· Wrote ")
                .Code(Path.Combine(".func", "config.json"))
                .Muted("."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Skeleton write failed; warn and let init succeed. The user can
            // create .func/config.json by hand if they want stack pinning.
            _interaction.WriteLine(l => l
                .Warning("! ")
                .Muted("Could not write .func/config.json (")
                .Code(ex.Message)
                .Muted(")."));
        }
    }

    private async Task<(string? Language, int? ErrorCode)> ResolveLanguageAsync(
        string? requested,
        IProjectInitializer initializer,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> supported = initializer.SupportedLanguages;
        if (supported.Count == 0)
        {
            return (requested, null);
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            string? match = supported.FirstOrDefault(l =>
                string.Equals(l, requested, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                _interaction.WriteError(
                    $"Language '{requested}' is not supported by the '{initializer.Stack}' stack. " +
                    $"Supported values: {string.Join(", ", supported)}.");
                return (null, 1);
            }

            return (match.ToLowerInvariant(), null);
        }

        if (supported.Count == 1)
        {
            return (supported[0].ToLowerInvariant(), null);
        }

        if (!_interaction.IsInteractive)
        {
            _interaction.WriteError(
                $"The '{initializer.Stack}' stack supports multiple languages. " +
                $"Re-run with --language <{string.Join("|", supported.Select(l => l.ToLowerInvariant()))}>.");
            return (null, 1);
        }

        string picked = await _interaction.PromptForSelectionAsync(
            "Select a language:",
            supported,
            cancellationToken);

        return (picked.ToLowerInvariant(), null);
    }

    private async Task<InitializerSelection> SelectInitializerAsync(string? requestedStack, CancellationToken cancellationToken)
    {
        string[] installed = [.. _initializers.Select(i => i.Stack)];

        if (_initializers.Count == 0)
        {
            return InitializerSelection.MissOnly(new WorkloadHint(
                WorkloadHintKind.NoWorkloadsInstalled, "initialize a project", null, installed));
        }

        if (!string.IsNullOrEmpty(requestedStack))
        {
            IProjectInitializer? match = _initializers.FirstOrDefault(i =>
                string.Equals(i.Stack, requestedStack, StringComparison.OrdinalIgnoreCase));
            return match is not null
                ? InitializerSelection.Picked(match)
                : InitializerSelection.MissOnly(new WorkloadHint(
                    WorkloadHintKind.NoMatchingStack, "initialize a project", requestedStack, installed));
        }

        // No stack specified: auto-select if there's only one initializer
        // installed (the common case for engineers using a single language).
        // Surface an info line so the user knows why this stack was picked.
        if (_initializers.Count == 1)
        {
            IProjectInitializer sole = _initializers[0];
            return InitializerSelection.PickedWithHint(
                sole,
                new WorkloadHint(
                    WorkloadHintKind.AutoSelectedSoleWorkload,
                    "initialize a project",
                    sole.Stack,
                    installed));
        }

        // Multiple initializers, non-interactive: bootstrap the skeleton and
        // tell the user to re-run with --stack. Scripts get a clear next-step
        // hint instead of a silently-picked first option.
        if (!_interaction.IsInteractive)
        {
            return InitializerSelection.MissOnly(new WorkloadHint(
                WorkloadHintKind.AmbiguousStackChoice, "initialize a project", null, installed));
        }

        // Multiple initializers, interactive: prompt.
        var choices = _initializers.Select(i => i.Stack).ToList();
        string picked = await _interaction.PromptForSelectionAsync(
            "Select a stack:",
            choices,
            cancellationToken);

        IProjectInitializer? promptedMatch = _initializers.FirstOrDefault(i => i.Stack == picked);
        return promptedMatch is not null
            ? InitializerSelection.Picked(promptedMatch)
            : InitializerSelection.MissOnly(new WorkloadHint(
                WorkloadHintKind.AmbiguousStackChoice, "initialize a project", null, installed));
    }

    private readonly record struct InitializerSelection(IProjectInitializer? Initializer, WorkloadHint? Hint)
    {
        public static InitializerSelection Picked(IProjectInitializer initializer) => new(initializer, null);

        public static InitializerSelection PickedWithHint(IProjectInitializer initializer, WorkloadHint hint) => new(initializer, hint);

        public static InitializerSelection MissOnly(WorkloadHint hint) => new(null, hint);
    }
}
