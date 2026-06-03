// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
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
/// <remarks>
/// When the target directory already contains a <c>host.json</c> but no
/// <c>.func/config.json</c>, the command "adopts" the project: it writes
/// the CLI config and skips scaffolding. The stack is taken from
/// <c>--stack</c> if provided, otherwise from <c>local.settings.json</c>'s
/// <c>FUNCTIONS_WORKER_RUNTIME</c>. If the resolved stack isn't installed,
/// adoption is refused with a pointer to <c>func setup</c>. When neither
/// signal is available the user is prompted interactively, or the command
/// refuses non-interactively. <c>--force</c> bypasses adopt mode entirely.
/// </remarks>
internal class InitCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> StackOption { get; } = new("--stack", "-s");

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
        Description = "Re-initialize the directory: deletes its contents (except .git) before scaffolding the new project."
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadHintRenderer _hintRenderer;
    private readonly ILocalSettingsProvider _localSettingsProvider;
    private readonly IReadOnlyList<IProjectInitializer> _initializers;

    public InitCommand(
        IInteractionService interaction,
        IWorkloadHintRenderer hintRenderer,
        ILocalSettingsProvider localSettingsProvider,
        IEnumerable<IProjectInitializer> initializers)
        : base("init", "Initialize a new Azure Functions project.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(hintRenderer);
        ArgumentNullException.ThrowIfNull(localSettingsProvider);
        ArgumentNullException.ThrowIfNull(initializers);

        _interaction = interaction;
        _hintRenderer = hintRenderer;
        _localSettingsProvider = localSettingsProvider;
        _initializers = initializers.ToList();

        StackOption.Description = BuildStackOptionDescription(_initializers);
        LanguageOption.Description = BuildLanguageOptionDescription(_initializers);

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        // Workload-contributed options are attached after built-ins so they
        // appear as a clearly-grouped block in --help output. Multiple workloads
        // may legitimately contribute the same option (e.g. --no-bundles for
        // every stack that emits an extension bundle). The registry de-dupes
        // by name and returns a single canonical instance to every workload,
        // so the option appears once in --help and every workload that reads
        // it back sees the same parsed value.
        var registry = new InitOptionRegistry(this);
        foreach (IProjectInitializer initializer in _initializers)
        {
            registry.SetActiveStack(initializer.Stack);
            initializer.GetInitOptions(registry);
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

        InitializationState state = DetectInitializationState(workingDirectory.Info);

        // .func/config.json already present means the project has been fully
        // initialized by v5; re-running without --force would silently no-op
        // the config write and re-scaffold on top of the user's code.
        if (state == InitializationState.FullyInitialized && !force)
        {
            _interaction.WriteError(
                $"This directory already contains a Functions project ('{CliConfigurationPathsOptions.ProjectConfigDisplayPath}' is present). " +
                "Pass --force to re-initialize.");
            return 1;
        }

        // host.json without .func/config.json means a pre-existing Functions
        // project that predates the v5 CLI config. Adopt it: write the config
        // so subsequent commands know the stack/language, but skip scaffolding
        // so we don't trample the user's source. --force still means "wipe and
        // re-scaffold" and takes the normal path.
        bool adoptExisting = state == InitializationState.AdoptableExistingProject && !force;

        // For adoption we honour what the project already declares.
        // Source: local.settings.json's FUNCTIONS_WORKER_RUNTIME.
        string? projectRuntime = adoptExisting ? ResolveProjectRuntime(workingDirectory.Info) : null;

        IProjectInitializer? initializer;
        if (adoptExisting)
        {
            // Resolve both signals to a concrete initializer (or null) using
            // alias-aware matching, so e.g. "dotnet-isolated" in
            // local.settings.json maps to the dotnet initializer and
            // `--stack dotnet` is recognized as agreeing with it.
            IProjectInitializer? requested = !string.IsNullOrWhiteSpace(requestedStack)
                ? FindInitializerForCandidate(requestedStack)
                : null;
            IProjectInitializer? declared = projectRuntime is not null
                ? FindInitializerForCandidate(projectRuntime)
                : null;

            // Explicit --stack that disagrees with the project's declared
            // runtime is almost certainly a mistake (it would produce a
            // .func/config.json that doesn't match what `func start` would
            // run). --force is the existing escape hatch. We only block on a
            // genuine conflict: when both signals resolve to known stacks
            // and those stacks differ. An uninstalled-vs-installed mismatch
            // is handled below by the "not installed" path.
            if (requested is not null
                && declared is not null
                && !string.Equals(requested.Stack, declared.Stack, StringComparison.OrdinalIgnoreCase))
            {
                _interaction.WriteError(
                    $"--stack '{requestedStack}' conflicts with the project's runtime '{projectRuntime}'. " +
                    "Re-run with the matching stack, or pass --force to override.");
                return 1;
            }

            // Pick a candidate: explicit --stack wins, else snap to the
            // project's declared runtime.
            string? candidate = !string.IsNullOrWhiteSpace(requestedStack) ? requestedStack : projectRuntime;
            initializer = requested ?? declared;

            if (candidate is not null && initializer is null)
            {
                // Candidate isn't an installed stack (and isn't an alias of
                // one). Refuse rather than write a config we can't validate.
                // The hint uses a `<stack>` placeholder on purpose: the
                // candidate string may itself be unknown (e.g. a typo or a
                // runtime value with no installed alias owner) and echoing
                // it would promise a `func setup` command that won't
                // necessarily work.
                _interaction.WriteError(
                    $"The '{candidate}' stack is not installed. " +
                    "Run `func setup --features <stack>` to setup your dev environment, then re-run `func init`.");
                return 1;
            }

            if (initializer is null)
            {
                // No project signal and no --stack. Don't auto-select: even
                // when only one initializer is installed, writing the wrong
                // stack to an existing project's .func/config.json is worse
                // than asking. Prompt when we can, refuse when we can't.
                initializer = await PromptForAdoptionStackAsync(cancellationToken);
                if (initializer is null)
                {
                    return 1;
                }
            }

            requestedStack = initializer.Stack;
        }
        else
        {
            initializer = await SelectInitializerAsync(requestedStack, cancellationToken);
            if (initializer is null)
            {
                return 1;
            }
        }

        // For adoption with no explicit --language, try to infer the language
        // from the project's on-disk shape (e.g. .csproj vs .fsproj, presence
        // of tsconfig.json). When the stack offers a language choice, this
        // keeps `.func/config.json` complete enough for downstream commands
        // (`func new --list` and friends) without forcing the user to repeat
        // information that's already visible in the project.
        if (adoptExisting && string.IsNullOrWhiteSpace(language))
        {
            language = initializer.DetectAdoptedLanguage(workingDirectory.Info);
        }

        // For adoption with no resolvable language, skip language resolution
        // entirely: the user already has code, and prompting / erroring for
        // a language they're not changing is just noise.
        if (!(adoptExisting && string.IsNullOrWhiteSpace(language)))
        {
            (string? resolved, int? errorCode) = await ResolveLanguageAsync(language, initializer, cancellationToken);
            if (errorCode is int code)
            {
                return code;
            }

            language = resolved;
        }

        if (force)
        {
            if (!await ConfirmClearDirectoryAsync(workingDirectory.Info, cancellationToken))
            {
                _interaction.WriteHint("Init cancelled. The directory was not modified.");
                return 1;
            }

            ClearDirectory(workingDirectory.Info);
        }

        // Only persist 'language' when the stack actually offers a choice.
        // For stacks with a single supported language, the stack runtime
        // already implies the language and the duplicate entry is noise.
        // For stacks with no declared language list, persist whatever the
        // caller supplied so we don't silently drop their input.
        string? persistedLanguage = initializer.SupportedLanguages.Count == 1 ? null : language;
        WriteCliConfigurationFile(workingDirectory.Info, initializer.Stack, persistedLanguage, force);
        _interaction.WriteBlankLine();

        if (adoptExisting)
        {
            _interaction.WriteLine(l => l
                .Success("✓ ")
                .Muted("Existing project adopted for ")
                .Code(initializer.Stack)
                .Muted("."));

            return 0;
        }

        var context = new InitContext(
            WorkingDirectory: workingDirectory,
            ProjectName: parseResult.GetValue(NameOption),
            Language: language,
            Force: force);

        await initializer.InitializeAsync(context, parseResult, cancellationToken);

        _interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Project initialized for ")
            .Code(initializer.Stack)
            .Muted("."));

        return 0;
    }

    private enum InitializationState
    {
        // No Functions project markers present; safe to scaffold from scratch.
        Empty,

        // host.json present but the v5 config (.func/config.json) is missing.
        // Treat as a pre-v5 project we can adopt by writing the config.
        AdoptableExistingProject,

        // .func/config.json is present. Already a v5 project; refuse without
        // --force.
        FullyInitialized,
    }

    protected override string HelpFooterHint =>
        "Looking for more stacks? Run `func workload search --stack` to list installable stack workloads.";

    // Builds the help text for `--stack`. Stacks come from installed
    // initializers' Stack ids, lowercased and sorted for a stable
    // presentation. Matching in SelectInitializerAsync is already
    // case-insensitive, so we surface the canonical lowercase form.
    private static string BuildStackOptionDescription(IReadOnlyList<IProjectInitializer> initializers)
    {
        var stacks = initializers
            .Select(i => i.Stack)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        return stacks.Count == 0
            ? "The stack to use. Set up a stack (`func setup --features <id>`) to see supported values."
            : "The stack to use. Supported values: " + string.Join(", ", stacks) + ".";
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
            ? "The programming language. Set up a stack (`func setup --features <id>`) to see supported values."
            : "The programming language. Supported values: " + string.Join(", ", languages) + ".";
    }

    // Warns about the destructive side of --force and (in interactive mode)
    // asks for confirmation. Non-interactive callers proceed without
    // prompting, on the theory that --force is itself an explicit opt-in.
    // Returns false only when the user declined the interactive prompt.
    private async Task<bool> ConfirmClearDirectoryAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken)
    {
        if (!DirectoryGuard.HasNonGitContent(workingDirectory))
        {
            return true;
        }

        _interaction.WriteWarning(
            $"--force will delete all files in '{workingDirectory.FullName}' (except .git) before initializing.");

        if (!_interaction.IsInteractive)
        {
            return true;
        }

        return await _interaction.ConfirmAsync("Continue?", defaultValue: false, cancellationToken);
    }

    // Wipes everything in the working directory before a --force re-init so
    // leftover files from the prior stack (node_modules, requirements.txt,
    // etc.) don't pollute the new project. Preserves .git so the user
    // doesn't lose history.
    private static void ClearDirectory(DirectoryInfo workingDirectory)
    {
        DirectoryGuard.ClearExceptGit(workingDirectory);
    }

    private static InitializationState DetectInitializationState(DirectoryInfo workingDirectory)
    {
        string config = CliConfigurationPathsOptions.GetProjectConfigPath(workingDirectory);
        if (File.Exists(config))
        {
            return InitializationState.FullyInitialized;
        }

        string hostJson = Path.Combine(workingDirectory.FullName, "host.json");
        if (File.Exists(hostJson))
        {
            return InitializationState.AdoptableExistingProject;
        }

        return InitializationState.Empty;
    }

    // Looks up the project's worker runtime from local.settings.json. The CLI
    // intentionally does not consult the FUNCTIONS_WORKER_RUNTIME process env
    // var here: adoption is about the on-disk project shape, and an env var
    // that happens to be set in the user's shell shouldn't change what we
    // write into .func/config.json.
    private string? ResolveProjectRuntime(DirectoryInfo workingDirectory)
    {
        string? raw = _localSettingsProvider.Get(workingDirectory).WorkerRuntime;
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    // Resolves a candidate string (from --stack or local.settings.json) to
    // an installed initializer. Matches against each initializer's canonical
    // Stack id first, then its WorkerRuntimeAliases (e.g. "dotnet-isolated"
    // → dotnet, "native" → go). Case-insensitive.
    private IProjectInitializer? FindInitializerForCandidate(string candidate)
    {
        return _initializers.FirstOrDefault(i =>
            string.Equals(i.Stack, candidate, StringComparison.OrdinalIgnoreCase)
            || i.WorkerRuntimeAliases.Any(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase)));
    }

    // Adoption-mode stack picker for the "no project signal" case. We never
    // auto-select here: in adoption, picking the wrong stack would write a
    // bad .func/config.json into a real project. Prompt when we're talking
    // to a human, refuse with an actionable error otherwise.
    private async Task<IProjectInitializer?> PromptForAdoptionStackAsync(CancellationToken cancellationToken)
    {
        string[] installed = [.. _initializers.Select(i => i.Stack)];

        if (_initializers.Count == 0)
        {
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.NoWorkloadsInstalled, "initialize a project", null, installed));
            return null;
        }

        if (!_interaction.IsInteractive)
        {
            _interaction.WriteError(
                "Couldn't detect the project's stack from local.settings.json. " +
                "Re-run with --stack <stack> to tell `func init` which stack to adopt.");
            return null;
        }

        var displayToInitializer = _initializers.ToDictionary(
            i => i.DisplayName,
            i => i,
            StringComparer.Ordinal);
        string picked = await _interaction.PromptForSelectionAsync(
            "Adopting an existing project. Which stack is it?",
            displayToInitializer.Keys,
            cancellationToken);

        return displayToInitializer.TryGetValue(picked, out IProjectInitializer? chosen) ? chosen : null;
    }

    private void WriteCliConfigurationFile(DirectoryInfo workingDirectory, string? stack, string? language, bool force)
    {
        string folder = CliConfigurationPathsOptions.GetProjectConfigFolderPath(workingDirectory);
        string path = CliConfigurationPathsOptions.GetProjectConfigPath(workingDirectory);

        // Treat an existing file as user-owned unless --force was passed.
        if (File.Exists(path) && !force)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);

            var stackConfig = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(stack))
            {
                stackConfig[CliConfigurationNames.StackRuntimeKey] = stack;
            }
            if (!string.IsNullOrWhiteSpace(language))
            {
                stackConfig[CliConfigurationNames.StackLanguageKey] = language;
            }

            var payload = new Dictionary<string, object>(StringComparer.Ordinal);
            if (stackConfig.Count > 0)
            {
                payload[CliConfigurationNames.StackSectionName] = stackConfig;
            }

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            File.WriteAllText(path, json + Environment.NewLine);

            _interaction.WriteLine(l => l
                .Muted("· Wrote ")
                .Code(CliConfigurationPathsOptions.ProjectConfigDisplayPath)
                .Muted("."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Skeleton write failed; warn and let init succeed. The user can
            // create .func/config.json by hand if they want stack pinning.
            _interaction.WriteLine(l => l
                .Warning("! ")
                .Muted($"Could not write {CliConfigurationPathsOptions.ProjectConfigDisplayPath} (")
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
            // First try direct match against canonical language names.
            string? match = supported.FirstOrDefault(l =>
                string.Equals(l, requested, StringComparison.OrdinalIgnoreCase));

            // If no direct match, try resolving as an alias.
            if (match is null)
            {
                match = ResolveAlias(requested, initializer.SupportedLanguageAliases);
            }

            if (match is null)
            {
                IEnumerable<string> allAliases = initializer.SupportedLanguageAliases.Values.SelectMany(v => v);
                _interaction.WriteError(
                    $"Language '{requested}' is not supported by the '{initializer.Stack}' stack. " +
                    $"Supported values: {string.Join(", ", supported)}. " +
                    $"Also accepted: {string.Join(", ", allAliases)}.");
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

        string picked = await _interaction.PromptForSelectionAsync("Select a language:", supported, cancellationToken);

        return (picked.ToLowerInvariant(), null);
    }

    private static string? ResolveAlias(string requested, IReadOnlyDictionary<string, IReadOnlyList<string>> aliases)
    {
        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in aliases)
        {
            if (entry.Value.Contains(requested, StringComparer.OrdinalIgnoreCase))
            {
                return entry.Key;
            }
        }

        return null;
    }

    private async Task<IProjectInitializer?> SelectInitializerAsync(string? requestedStack, CancellationToken cancellationToken)
    {
        string[] installed = [.. _initializers.Select(i => i.Stack)];

        if (_initializers.Count == 0)
        {
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.NoWorkloadsInstalled, "initialize a project", null, installed));
            return null;
        }

        if (!string.IsNullOrEmpty(requestedStack))
        {
            IProjectInitializer? match = _initializers.FirstOrDefault(i =>
                string.Equals(i.Stack, requestedStack, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.NoMatchingStack, "initialize a project", requestedStack, installed));
            return null;
        }

        // No --stack specified: auto-select when there's only one initializer.
        if (_initializers.Count == 1)
        {
            IProjectInitializer sole = _initializers[0];
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.AutoSelectedSoleWorkload, "initialize a project", sole.Stack, installed));
            return sole;
        }

        // Multiple initializers, non-interactive: tell the user to re-run with --stack.
        if (!_interaction.IsInteractive)
        {
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.AmbiguousStackChoice, "initialize a project", null, installed));
            return null;
        }

        // Multiple initializers, interactive: prompt using display names,
        // then map the selection back to its initializer.
        var displayToInitializer = _initializers.ToDictionary(
            i => i.DisplayName,
            i => i,
            StringComparer.Ordinal);
        string picked = await _interaction.PromptForSelectionAsync("Select a stack:", displayToInitializer.Keys, cancellationToken);

        return displayToInitializer.TryGetValue(picked, out IProjectInitializer? chosen) ? chosen : null;
    }
}
