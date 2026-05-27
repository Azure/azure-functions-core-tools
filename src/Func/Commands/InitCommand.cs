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
        Description = "Re-initialize the directory: deletes its contents (except .git) before scaffolding the new project."
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

        // Refuse to overwrite an existing Functions project. Either host.json
        // or .func/config.json being present is enough to count: both are
        // Functions project skeleton files we'd otherwise rewrite. --force opts in
        // to a re-init.
        if (!force && IsAlreadyInitialized(workingDirectory.Info, out string existingFile))
        {
            _interaction.WriteError(
                $"This directory already contains a Functions project ('{existingFile}' is present). " +
                "Pass --force to re-initialize.");
            return 1;
        }

        IProjectInitializer? initializer = await SelectInitializerAsync(requestedStack, cancellationToken);

        if (initializer is null)
        {
            return 1;
        }

        (string? resolved, int? errorCode) = await ResolveLanguageAsync(language, initializer, cancellationToken);
        if (errorCode is int code)
        {
            return code;
        }

        language = resolved;

        if (force)
        {
            if (!await ConfirmClearDirectoryAsync(workingDirectory.Info, cancellationToken))
            {
                _interaction.WriteHint("Init cancelled. The directory was not modified.");
                return 1;
            }

            ClearDirectory(workingDirectory.Info);
        }

        WriteCliConfigurationFile(workingDirectory.Info, initializer.Stack, language, force);
        _interaction.WriteBlankLine();

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

    private static bool IsAlreadyInitialized(DirectoryInfo workingDirectory, out string existingFile)
    {
        string hostJson = Path.Combine(workingDirectory.FullName, "host.json");
        if (File.Exists(hostJson))
        {
            existingFile = "host.json";
            return true;
        }

        string config = CliConfigurationPathsOptions.GetProjectConfigPath(workingDirectory);
        if (File.Exists(config))
        {
            existingFile = Path.GetRelativePath(workingDirectory.FullName, config);
            return true;
        }

        existingFile = string.Empty;
        return false;
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

        string picked = await _interaction.PromptForSelectionAsync(
            "Select a language:",
            supported,
            cancellationToken);

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
        string picked = await _interaction.PromptForSelectionAsync(
            "Select a stack:",
            displayToInitializer.Keys,
            cancellationToken);

        return displayToInitializer.TryGetValue(picked, out IProjectInitializer? chosen) ? chosen : null;
    }
}
