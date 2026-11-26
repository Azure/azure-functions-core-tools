// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// Steps shared by the <c>func init</c> flows: stack and language resolution, destination preparation,
/// <c>.func/config.json</c> persistence, and success reporting.
/// </summary>
internal sealed class InitWorkflow
{
    private static readonly JsonSerializerOptions _projectConfigReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadHintRenderer _hintRenderer;
    private readonly ILocalSettingsProvider _localSettingsProvider;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly IHostJsonBundleSectionReader _bundleSectionReader;
    private readonly InstalledBundleScanner _bundleScanner;

    public InitWorkflow(
        IInteractionService interaction,
        IWorkloadHintRenderer hintRenderer,
        ILocalSettingsProvider localSettingsProvider,
        IFunctionsProjectResolver projectResolver,
        IHostJsonBundleSectionReader bundleSectionReader,
        InstalledBundleScanner bundleScanner,
        IReadOnlyList<IProjectInitializer> initializers)
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _hintRenderer = hintRenderer ?? throw new ArgumentNullException(nameof(hintRenderer));
        _localSettingsProvider = localSettingsProvider
            ?? throw new ArgumentNullException(nameof(localSettingsProvider));
        _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
        _bundleSectionReader = bundleSectionReader ?? throw new ArgumentNullException(nameof(bundleSectionReader));
        _bundleScanner = bundleScanner ?? throw new ArgumentNullException(nameof(bundleScanner));
        Initializers = initializers ?? throw new ArgumentNullException(nameof(initializers));
    }

    public IInteractionService Interaction => _interaction;

    public IReadOnlyList<IProjectInitializer> Initializers { get; }

    public static InitializationState DetectInitializationState(DirectoryInfo workingDirectory)
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
    // language fields. Returns null when the file can't be read or parsed;
    // callers then treat the project as fully initialized and require --force.
    public static (string? Runtime, string? Language)? TryReadExistingStack(DirectoryInfo workingDirectory)
    {
        string path = CliConfigurationPathsOptions.GetProjectConfigPath(workingDirectory);
        try
        {
            using FileStream stream = File.OpenRead(path);
            ProjectConfigFile? config =
                JsonSerializer.Deserialize<ProjectConfigFile>(stream, _projectConfigReadOptions);
            return (config?.Stack?.Runtime, config?.Stack?.Language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // Looks up the legacy FUNCTIONS_WORKER_RUNTIME value from local.settings.json
    // (not the stack in .func/config.json). The CLI intentionally does not
    // consult the FUNCTIONS_WORKER_RUNTIME process env var here: adoption is
    // about the on-disk project shape, and an env var that happens to be set in
    // the user's shell shouldn't change what we write into .func/config.json.
    public string? ResolveLegacyWorkerRuntime(DirectoryInfo workingDirectory)
    {
        string? raw = _localSettingsProvider.Get(workingDirectory).WorkerRuntime;
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    // Returns null when the resolver can't classify the directory or the
    // resolved project doesn't surface a Language; adoption then writes the
    // stack runtime without a language entry.
    public async Task<string?> DetectAdoptedLanguageAsync(
        WorkingDirectory workingDirectory,
        CancellationToken cancellationToken)
    {
        ProjectResolutionResult resolution = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(workingDirectory),
            cancellationToken);

        return resolution is ProjectResolutionResult.Resolved resolved ? resolved.Project.Language : null;
    }

    // Resolves a candidate string (from --stack or local.settings.json) to
    // an installed initializer. Matches against each initializer's canonical
    // Stack id first, then its WorkerRuntimeAliases (e.g. "dotnet-isolated"
    // → dotnet, "native" → go). Case-insensitive.
    public IProjectInitializer? FindInitializerForCandidate(string candidate)
    {
        return Initializers.FirstOrDefault(i =>
            string.Equals(i.Stack, candidate, StringComparison.OrdinalIgnoreCase)
            || i.WorkerRuntimeAliases.Any(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase)));
    }

    // Adoption-mode stack picker for the "no project signal" case. We never
    // auto-select here: in adoption, picking the wrong stack would write a
    // bad .func/config.json into a real project. Prompt when we're talking
    // to a human, refuse with an actionable error otherwise.
    public async Task<IProjectInitializer?> PromptForAdoptionStackAsync(CancellationToken cancellationToken)
    {
        string[] installed = [.. Initializers.Select(i => i.Stack)];

        if (Initializers.Count == 0)
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

        var displayToInitializer = Initializers.ToDictionary(i => i.DisplayName, i => i, StringComparer.Ordinal);
        string picked = await _interaction.PromptForSelectionAsync(
            "Adopting an existing project. Which stack is it?",
            displayToInitializer.Keys,
            cancellationToken);

        return displayToInitializer.TryGetValue(picked, out IProjectInitializer? chosen) ? chosen : null;
    }

    public async Task<IProjectInitializer?> SelectInitializerAsync(
        string? requestedStack,
        CancellationToken cancellationToken)
    {
        string[] installed = [.. Initializers.Select(i => i.Stack)];

        if (Initializers.Count == 0)
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
        if (Initializers.Count == 1)
        {
            IProjectInitializer sole = Initializers[0];
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
        var displayToInitializer = Initializers.ToDictionary(i => i.DisplayName, i => i, StringComparer.Ordinal);
        string picked = await _interaction.PromptForSelectionAsync(
            "Select a stack:",
            displayToInitializer.Keys,
            cancellationToken);

        return displayToInitializer.TryGetValue(picked, out IProjectInitializer? chosen) ? chosen : null;
    }

    public async Task<(string? Language, int? ErrorCode)> ResolveLanguageAsync(
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
            match ??= ResolveAlias(requested, initializer.SupportedLanguageAliases);

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

    // Only persist 'language' when the stack actually offers a choice.
    // For stacks with a single supported language, the stack runtime
    // already implies the language and the duplicate entry is noise.
    // For stacks with no declared language list, persist whatever the
    // caller supplied so we don't silently drop their input.
    public static string? GetPersistedLanguage(IProjectInitializer initializer, string? language)
        => initializer.SupportedLanguages.Count == 1 ? null : language;

    public async Task<bool> PrepareDestinationAsync(InitContext context, CancellationToken cancellationToken)
    {
        if (!context.Force)
        {
            return true;
        }

        context.WorkingDirectory.CreateIfNotExists();
        if (!await ConfirmClearDirectoryAsync(context.WorkingDirectory.Info, cancellationToken))
        {
            _interaction.WriteHint("Init cancelled. The directory was not modified.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ClearDirectory(context.WorkingDirectory.Info);
        return true;
    }

    public async Task<int> ReportInitializedAsync(
        DirectoryInfo projectDirectory,
        string stack,
        CancellationToken cancellationToken)
    {
        _interaction.WriteLine(l => l.Success("✓ ").Muted("Project initialized for ").Code(stack).Muted("."));
        await WarnIfBundleChannelNotInstalledAsync(projectDirectory, cancellationToken);
        return 0;
    }

    public int RejectUnmatchedTokens(IReadOnlyList<string> unmatchedTokens)
    {
        string token = unmatchedTokens[0];
        _interaction.WriteError(token.StartsWith('-')
            ? $"Unrecognized option '{token}'."
            : $"Unrecognized argument '{token}'. Specify the project path before template or stack options.");
        return 1;
    }

    public void WriteCliConfigurationFile(DirectoryInfo workingDirectory, string? stack, string? language, bool force)
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

            Dictionary<string, string> stackConfig = [with(StringComparer.Ordinal)];
            if (!string.IsNullOrWhiteSpace(stack))
            {
                stackConfig[CliConfigurationNames.StackRuntimeKey] = stack;
            }
            if (!string.IsNullOrWhiteSpace(language))
            {
                stackConfig[CliConfigurationNames.StackLanguageKey] = language;
            }

            Dictionary<string, object> payload = [with(StringComparer.Ordinal)];
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
    public void MergeLanguageIntoConfig(DirectoryInfo workingDirectory, string? language)
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

    // A fresh scaffold may pin a preview/experimental bundle channel (via `-c`).
    // Warn when no bundle workload for that channel is installed, pointing at
    // workload search so `func start` doesn't later fail to resolve the bundle.
    private async Task WarnIfBundleChannelNotInstalledAsync(
        DirectoryInfo projectDirectory,
        CancellationToken cancellationToken)
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
            $"This project targets the '{channel.ToDisplayString()}' extension bundle channel, "
            + "but no matching bundle workload is installed. "
            + "Find one with: func workload search bundles --prerelease");
    }

    // --force authorizes deletion when input is unavailable, but a prompt
    // still defaults to No whenever confirmation can read an answer.
    private async Task<bool> ConfirmClearDirectoryAsync(
        DirectoryInfo workingDirectory,
        CancellationToken cancellationToken)
    {
        if (!DirectoryGuard.HasNonGitContent(workingDirectory))
        {
            return true;
        }

        _interaction.WriteWarning(
            $"--force will delete all files in '{workingDirectory.FullName}' (except a .git directory) "
            + "before initializing.");

        return await _interaction.ConfirmAsync(
            "Continue?", defaultValue: false, whenInputUnavailable: true, cancellationToken);
    }

    // Wipes everything in the working directory before a --force re-init so
    // leftover files from the prior stack (node_modules, requirements.txt,
    // etc.) don't pollute the new project. Preserves a .git directory so the
    // user doesn't lose history; a .git pointer file (worktree) is not preserved.
    private static void ClearDirectory(DirectoryInfo workingDirectory)
    {
        DirectoryGuard.ClearExceptGit(workingDirectory);
    }

    // Shape of .func/config.json read by TryReadExistingStack; unknown sections are ignored.
    private sealed record ProjectConfigFile(StackOptions? Stack);
}
