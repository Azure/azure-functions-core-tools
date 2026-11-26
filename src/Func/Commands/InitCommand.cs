// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
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
/// When <c>.func/config.json</c> exists but is missing <c>stack.language</c>
/// on a multi-language stack, the command "heals" it: it detects the
/// language via the project resolver and merges the missing key in,
/// preserving every other key in the file. <c>--force</c> still wipes.
/// </remarks>
internal class InitCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> StackOption { get; } = new("--stack", "-s");

    public Option<string?> NameOption { get; } = new("--name", "-n")
    {
        Description = "The name of the Function App project"
    };

    public Option<string?> LanguageOption { get; } = new("--language", "-l");

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Re-initialize the directory: deletes its contents (except .git) before scaffolding the new project."
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadHintRenderer _hintRenderer;
    private readonly ILocalSettingsProvider _localSettingsProvider;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly IHostJsonBundleSectionReader _bundleSectionReader;
    private readonly InstalledBundleScanner _bundleScanner;
    private readonly IReadOnlyList<IProjectInitializer> _initializers;

    public InitCommand(
        IInteractionService interaction,
        IWorkloadHintRenderer hintRenderer,
        ILocalSettingsProvider localSettingsProvider,
        IFunctionsProjectResolver projectResolver,
        IHostJsonBundleSectionReader bundleSectionReader,
        InstalledBundleScanner bundleScanner,
        IEnumerable<IProjectInitializer> initializers)
        : base("init", "Initialize a new Azure Functions project.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(hintRenderer);
        ArgumentNullException.ThrowIfNull(localSettingsProvider);
        ArgumentNullException.ThrowIfNull(projectResolver);
        ArgumentNullException.ThrowIfNull(bundleSectionReader);
        ArgumentNullException.ThrowIfNull(bundleScanner);
        ArgumentNullException.ThrowIfNull(initializers);

        _interaction = interaction;
        _hintRenderer = hintRenderer;
        _localSettingsProvider = localSettingsProvider;
        _projectResolver = projectResolver;
        _bundleSectionReader = bundleSectionReader;
        _bundleScanner = bundleScanner;
        _initializers = initializers.ToList();

        StackOption.Description = BuildOptionDescription("The stack to use", _initializers.Select(i => i.Stack.Descriptor.Identifier));
        LanguageOption.Description = BuildOptionDescription("The programming language", _initializers.SelectMany(i => i.Stack.SupportedLanguages).Select(l => l.LanguageIdentifier));

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        // The registry shares canonical instances across workloads; help uses
        // their ownership to distinguish shared options from stack-specific ones.
        var registry = new InitOptionRegistry(this);
        foreach (IProjectInitializer initializer in _initializers)
        {
            StackDescriptor descriptor = initializer.Stack.Descriptor;
            registry.SetActiveStack(descriptor.Identifier);
            IReadOnlyList<Option> options = initializer.GetInitOptions(registry);
            // Bundle options stay general even when only one bundle-using stack is installed.
            RegisterStackOptionHelp(descriptor, options.Where(option => option.Name != CommonInitOptions.NoBundleOptionName
                && option.Name != CommonInitOptions.BundlesChannelOptionName));
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
            .GroupBy(i => i.Stack.Descriptor)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Identifier)
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
        string? requestedLanguage = parseResult.GetValue(LanguageOption);
        string? requestedStack = parseResult.GetValue(StackOption);

        InitializationState state = DetectInitializationState(workingDirectory.Info);
        string? existingRuntime = null;

        // Heal one specific partial state: config exists with a runtime but no
        // language on a multi-language stack. Detection here happens before the
        // FullyInitialized refuse so the user doesn't have to reach for --force
        // (which would wipe profiles, custom keys, etc.) just to fill in a
        // missing language field. Other partial shapes still require --force.
        if (state == InitializationState.FullyInitialized
            && !force
            && TryReadExistingStack(workingDirectory.Info) is { } snapshot
            && !string.IsNullOrWhiteSpace(snapshot.Runtime)
            && string.IsNullOrWhiteSpace(snapshot.Language)
            && FindInitializerForCandidate(snapshot.Runtime) is { Stack.SupportedLanguages.Count: > 1 })
        {
            state = InitializationState.PartiallyInitialized;
            existingRuntime = snapshot.Runtime;
        }

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
        bool healExisting = state == InitializationState.PartiallyInitialized && !force;

        // Where the project's stack runtime comes from: heal trusts what's
        // already in .func/config.json; adopt reads local.settings.json
        // (FUNCTIONS_WORKER_RUNTIME); the default path doesn't snap to either.
        string? projectRuntime = healExisting
            ? existingRuntime
            : adoptExisting ? ResolveProjectRuntime(workingDirectory.Info) : null;

        IProjectInitializer? initializer;
        if (adoptExisting || healExisting)
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
                && requested.Stack.Descriptor != declared.Stack.Descriptor)
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
        }
        else
        {
            initializer = await SelectInitializerAsync(requestedStack, cancellationToken);
            if (initializer is null)
            {
                return 1;
            }
        }

        IProjectStack stack = initializer.Stack;
        LanguageDescriptor? language = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(requestedLanguage))
            {
                language = InstalledProjectStackCatalog.ResolveLanguage(stack, requestedLanguage, "--language");
            }
            else if (adoptExisting || healExisting)
            {
                language = await DetectAdoptedLanguageAsync(workingDirectory, stack, cancellationToken);
            }

            if (language is null && !adoptExisting)
            {
                language = await SelectLanguageAsync(stack, cancellationToken);
            }
        }
        catch (ProjectStackResolutionException ex)
        {
            _interaction.WriteError(ex.Message);
            return 1;
        }

        if (force)
        {
            if (!await ConfirmClearDirectoryAsync(workingDirectory.Info, cancellationToken))
            {
                _interaction.WriteHint("Init cancelled. The directory was not modified.");
                return 1;
            }

            cancellationToken.ThrowIfCancellationRequested();
            ClearDirectory(workingDirectory.Info);
        }

        // Only persist 'language' when the stack actually offers a choice.
        // For stacks with a single supported language, the stack runtime
        // already implies the language and the duplicate entry is noise.
        string? persistedLanguage = stack.SupportedLanguages.Count == 1 ? null : language?.LanguageIdentifier;

        if (healExisting)
        {
            MergeLanguageIntoConfig(workingDirectory.Info, persistedLanguage);
            _interaction.WriteBlankLine();
            _interaction.WriteLine(l => l
                .Success("✓ ")
                .Muted("Existing project completed for ")
                .Code(stack.Descriptor.DisplayName)
                .Muted("."));
            return 0;
        }

        WriteCliConfigurationFile(workingDirectory.Info, stack.Descriptor.Identifier, persistedLanguage, force);
        _interaction.WriteBlankLine();

        if (adoptExisting)
        {
            _interaction.WriteLine(l => l
                .Success("✓ ")
                .Muted("Existing project adopted for ")
                .Code(stack.Descriptor.DisplayName)
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
            .Code(stack.Descriptor.DisplayName)
            .Muted("."));

        await WarnIfBundleChannelNotInstalledAsync(workingDirectory.Info, cancellationToken);

        return 0;
    }

    // A fresh scaffold may pin a preview/experimental bundle channel (via `-c`).
    // Warn when no bundle workload for that channel is installed, pointing at
    // workload search so `func start` doesn't later fail to resolve the bundle.
    private async Task WarnIfBundleChannelNotInstalledAsync(DirectoryInfo projectDirectory, CancellationToken cancellationToken)
    {
        HostJsonBundleSection? bundle = await _bundleSectionReader.ReadAsync(projectDirectory, cancellationToken);
        if (bundle is null
            || !BundleHelpers.TryGetBundleChannel(bundle.Id, out BundleChannel channel)
            || channel == BundleChannel.Stable)
        {
            return;
        }

        ScanResult scan = await _bundleScanner.ScanAsync(channel, cancellationToken);
        if (scan.FilteredVersions.Count > 0)
        {
            return;
        }

        _interaction.WriteWarning(
            $"This project targets the '{channel.ToDisplayString()}' extension bundle channel, but no matching bundle workload is installed. "
            + "Find one with: func workload search bundles --prerelease");
    }

    private enum InitializationState
    {
        // No Functions project markers present; safe to scaffold from scratch.
        Empty,

        // host.json present but the v5 config (.func/config.json) is missing.
        // Treat as a pre-v5 project we can adopt by writing the config.
        AdoptableExistingProject,

        // .func/config.json exists with a runtime but no language on a
        // multi-language stack. Run the adopt-mode language detector and
        // merge the result back into the existing config without touching
        // other keys. Single-language stacks never enter this state because
        // language is intentionally omitted for them.
        PartiallyInitialized,

        // .func/config.json is present and complete. Refuse without --force.
        FullyInitialized,
    }

    protected override string HelpFooterHint =>
        "Looking for more stacks? Run `func workload search --stack` to list installable stack workloads.";

    private static string BuildOptionDescription(string description, IEnumerable<string> values)
    {
        string supported = string.Join(", ", values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        return supported.Length == 0 ? $"{description}. Set up a stack (`func setup --features <id>`) to see supported values."
            : $"{description}. Supported values: {supported}.";
    }

    // --force authorizes deletion when input is unavailable, but a prompt
    // still defaults to No whenever confirmation can read an answer.
    private async Task<bool> ConfirmClearDirectoryAsync(DirectoryInfo workingDirectory, CancellationToken cancellationToken)
    {
        if (!DirectoryGuard.HasNonGitContent(workingDirectory))
        {
            return true;
        }

        _interaction.WriteWarning(
            $"--force will delete all files in '{workingDirectory.FullName}' (except .git) before initializing.");

        return await _interaction.ConfirmAsync("Continue?", defaultValue: false, whenInputUnavailable: true, cancellationToken);
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

    // Reads the existing .func/config.json and returns its stack runtime /
    // language fields. Returns null when the file can't be parsed; callers
    // then treat the project as fully initialized and require --force.
    private static (string? Runtime, string? Language)? TryReadExistingStack(DirectoryInfo workingDirectory)
    {
        string path = CliConfigurationPathsOptions.GetProjectConfigPath(workingDirectory);
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty(CliConfigurationNames.StackSectionName, out JsonElement stack)
                || stack.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            string? runtime = stack.TryGetProperty(CliConfigurationNames.StackRuntimeKey, out JsonElement r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;
            string? lang = stack.TryGetProperty(CliConfigurationNames.StackLanguageKey, out JsonElement l) && l.ValueKind == JsonValueKind.String
                ? l.GetString()
                : null;
            return (runtime, lang);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
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

    // Returns null when the resolver can't classify the directory or the
    // resolved project doesn't surface a Language; adoption then writes the
    // stack runtime without a language entry.
    private async Task<LanguageDescriptor?> DetectAdoptedLanguageAsync(WorkingDirectory workingDirectory, IProjectStack stack, CancellationToken cancellationToken)
    {
        ProjectResolutionResult resolution = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(workingDirectory),
            cancellationToken);

        if (resolution is not ProjectResolutionResult.Resolved { Project.Language: { } language })
        {
            return null;
        }

        return stack.SupportedLanguages.SingleOrDefault(l => l == language) ?? throw new ProjectStackResolutionException(
            $"Detected project language '{language.LanguageIdentifier}' is not supported by stack '{stack.Descriptor.Identifier}'.");
    }

    // Both --stack and an existing project can supply the CLI ID or the host runtime (e.g. "native" for Go).
    private IProjectInitializer? FindInitializerForCandidate(string candidate)
    {
        return _initializers.FirstOrDefault(i =>
            string.Equals(i.Stack.Descriptor.Identifier, candidate, StringComparison.OrdinalIgnoreCase)
            || string.Equals(i.Stack.Descriptor.WorkerRuntime, candidate, StringComparison.OrdinalIgnoreCase));
    }

    // Adoption-mode stack picker for the "no project signal" case. We never
    // auto-select here: in adoption, picking the wrong stack would write a
    // bad .func/config.json into a real project. Prompt when we're talking
    // to a human, refuse with an actionable error otherwise.
    private async Task<IProjectInitializer?> PromptForAdoptionStackAsync(CancellationToken cancellationToken)
    {
        string[] installed = [.. _initializers.Select(i => i.Stack.Descriptor.Identifier)];

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

        return await PromptForInitializerAsync("Adopting an existing project. Which stack is it?", cancellationToken);
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

    // Patches stack.language into an existing .func/config.json without
    // touching any other key. Used for the PartiallyInitialized heal path so
    // users keep their profiles / custom keys instead of reaching for --force.
    private void MergeLanguageIntoConfig(DirectoryInfo workingDirectory, string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        string path = CliConfigurationPathsOptions.GetProjectConfigPath(workingDirectory);
        try
        {
            string existing = File.ReadAllText(path);
            JsonObject root = JsonNode.Parse(existing) as JsonObject ?? [];
            JsonObject stack = root[CliConfigurationNames.StackSectionName] as JsonObject ?? [];
            stack[CliConfigurationNames.StackLanguageKey] = language;
            root[CliConfigurationNames.StackSectionName] = stack;

            string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json + Environment.NewLine);

            _interaction.WriteLine(l => l
                .Muted("· Updated ")
                .Code(CliConfigurationPathsOptions.ProjectConfigDisplayPath)
                .Muted($" with language: {language}."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _interaction.WriteLine(l => l
                .Warning("! ")
                .Muted($"Could not update {CliConfigurationPathsOptions.ProjectConfigDisplayPath} (")
                .Code(ex.Message)
                .Muted(")."));
        }
    }

    private async Task<LanguageDescriptor> SelectLanguageAsync(IProjectStack stack, CancellationToken cancellationToken)
    {
        IReadOnlyList<LanguageDescriptor> choices = stack.SupportedLanguages;
        if (choices.Count == 0)
        {
            throw new ProjectStackResolutionException($"Stack '{stack.Descriptor.Identifier}' must declare at least one supported language.");
        }
        if (choices is [var only])
        {
            return only;
        }
        if (!_interaction.IsInteractive)
        {
            throw new ProjectStackResolutionException($"The '{stack.Descriptor.Identifier}' stack supports multiple languages. Re-run with --language <{string.Join("|", choices.Select(l => l.LanguageIdentifier))}>.");
        }

        return await _interaction.PromptForSelectionAsync("Select a language:", choices,
            l => choices.Count(c => c.DisplayName.Equals(l.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1 ? $"{l.DisplayName} ({l.LanguageIdentifier})" : l.DisplayName,
            cancellationToken);
    }

    private async Task<IProjectInitializer?> SelectInitializerAsync(string? requestedStack, CancellationToken cancellationToken)
    {
        string[] installed = [.. _initializers.Select(i => i.Stack.Descriptor.Identifier)];

        if (_initializers.Count == 0)
        {
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.NoWorkloadsInstalled, "initialize a project", null, installed));
            return null;
        }

        if (!string.IsNullOrEmpty(requestedStack))
        {
            IProjectInitializer? match = FindInitializerForCandidate(requestedStack);
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
                WorkloadHintKind.AutoSelectedSoleWorkload, "initialize a project", sole.Stack.Descriptor.Identifier, installed));
            return sole;
        }

        // Multiple initializers, non-interactive: tell the user to re-run with --stack.
        if (!_interaction.IsInteractive)
        {
            _hintRenderer.Render(new WorkloadHint(
                WorkloadHintKind.AmbiguousStackChoice, "initialize a project", null, installed));
            return null;
        }

        return await PromptForInitializerAsync("Select a stack:", cancellationToken);
    }

    private async Task<IProjectInitializer> PromptForInitializerAsync(string title, CancellationToken cancellationToken)
    {
        List<StackDescriptor> choices = [.. _initializers.Select(i => i.Stack.Descriptor)];
        StackDescriptor picked = await _interaction.PromptForSelectionAsync(title, choices,
            d => choices.Count(c => c.DisplayName.Equals(d.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1 ? $"{d.DisplayName} ({d.Identifier})" : d.DisplayName,
            cancellationToken);
        return _initializers.Single(i => i.Stack.Descriptor == picked);
    }
}
