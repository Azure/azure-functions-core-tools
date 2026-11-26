// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Templating;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// How <c>func init</c> handles the target directory.
/// </summary>
internal enum InitMode
{
    /// <summary>
    /// Writes <c>.func/config.json</c>, then creates project content.
    /// </summary>
    Create,

    /// <summary>
    /// Writes <c>.func/config.json</c> for a project with <c>host.json</c> but no CLI config.
    /// </summary>
    Adopt,

    /// <summary>
    /// Adds the missing language to <c>.func/config.json</c> for a multi-language stack.
    /// </summary>
    Complete,
}

/// <summary>
/// The stack, language and mode <c>func init</c> resolved before any route writes.
/// </summary>
internal sealed record InitResolution(IProjectInitializer Initializer, LanguageDescriptor Language, InitMode Mode)
{
    public IProjectStack Stack => Initializer.Stack;
}

/// <summary>
/// Steps shared by the <c>func init</c> routes: stack and language validation against the installed stack
/// descriptors, destination preparation, <c>.func/config.json</c> persistence through
/// <see cref="IProjectStackConfiguration"/>, and success reporting.
/// </summary>
internal sealed class InitWorkflow(
    IInteractionService interaction,
    IWorkloadHintRenderer hintRenderer,
    IProjectStackConfiguration configuration,
    InstalledProjectStackCatalog catalog,
    IFunctionsProjectResolver projectResolver,
    IHostJsonBundleSectionReader bundleSectionReader,
    InstalledBundleScanner bundleScanner,
    IReadOnlyList<IProjectInitializer> initializers)
{
    private readonly IInteractionService _interaction = interaction
        ?? throw new ArgumentNullException(nameof(interaction));
    private readonly IWorkloadHintRenderer _hintRenderer = hintRenderer
        ?? throw new ArgumentNullException(nameof(hintRenderer));
    private readonly InstalledProjectStackCatalog _catalog = catalog
        ?? throw new ArgumentNullException(nameof(catalog));
    private readonly IFunctionsProjectResolver _projectResolver = projectResolver
        ?? throw new ArgumentNullException(nameof(projectResolver));
    private readonly IHostJsonBundleSectionReader _bundleSectionReader = bundleSectionReader
        ?? throw new ArgumentNullException(nameof(bundleSectionReader));
    private readonly InstalledBundleScanner _bundleScanner = bundleScanner
        ?? throw new ArgumentNullException(nameof(bundleScanner));

    public IInteractionService Interaction => _interaction;

    /// <summary>
    /// The only reader and writer of <c>.func/config.json</c> for <c>func init</c>.
    /// </summary>
    public IProjectStackConfiguration Configuration { get; } = configuration
        ?? throw new ArgumentNullException(nameof(configuration));

    public IReadOnlyList<IProjectInitializer> Initializers { get; } = initializers
        ?? throw new ArgumentNullException(nameof(initializers));

    /// <summary>
    /// Validates the stack and language for <paramref name="request"/> and picks the mode, before any route writes.
    /// </summary>
    /// <returns>The resolution, or <see langword="null"/> after an error or hint has been written.</returns>
    /// <remarks>
    /// <para>
    /// The stack comes from <c>--stack</c>, then from the project: <c>stack.runtime</c> in <c>.func/config.json</c>,
    /// otherwise <c>FUNCTIONS_WORKER_RUNTIME</c> in <c>local.settings.json</c>, matched against each installed stack's
    /// worker runtime. A value that doesn't identify an installed stack, or that conflicts with <c>--stack</c>, is
    /// refused. With no signal, a sole installed stack is selected for a new project; otherwise the user is prompted
    /// interactively, or the command refuses non-interactively. An existing project is never auto-selected.
    /// </para>
    /// <para>
    /// The language comes from <c>--language</c>, the persisted language, or the project detected for the stack, and
    /// must agree with each of them. A multi-language stack without one prompts interactively or is refused.
    /// </para>
    /// </remarks>
    /// <exception cref="ProjectStackResolutionException">The stack or language can't be resolved.</exception>
    /// <exception cref="ProjectStackConfigurationException">The project configuration can't be read.</exception>
    public async Task<InitResolution?> ResolveAsync(InitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkingDirectory directory = request.WorkingDirectory;
        ProjectStackSettings settings = request.Force ? new() : Configuration.Read(directory.Info);

        // No project content is created for an existing project, so options only a template or stack scaffolding
        // consumes are rejected before any prompt or write.
        if (settings.IsAdoptable && request.UnmatchedTokens.Count > 0)
        {
            RejectExistingProjectTokens(request);
            return null;
        }

        IProjectInitializer? initializer = await SelectInitializerAsync(
            request.RequestedStack, settings, cancellationToken);
        if (initializer is null)
        {
            return null;
        }

        IProjectStack stack = initializer.Stack;
        string? explicitLanguage = request.Language;
        LanguageDescriptor? language = explicitLanguage is null
            ? null
            : InstalledProjectStackCatalog.ResolveLanguage(stack, explicitLanguage, "--language");
        if (settings.Language is { } persistedLanguage)
        {
            LanguageDescriptor persisted = InstalledProjectStackCatalog.ResolveLanguage(
                stack, persistedLanguage, ProjectStackSettings.LanguageSource);
            if (language is not null && persisted != language)
            {
                throw new ProjectStackResolutionException(
                    $"--language '{explicitLanguage}' conflicts with {ProjectStackSettings.LanguageSource} " +
                    $"'{persistedLanguage}'.");
            }
        }

        bool complete = settings.HasProjectConfig && settings.Language is null && stack.SupportedLanguages.Count > 1;
        if (settings.HasProjectConfig && !complete)
        {
            _interaction.WriteError(
                "This directory already contains a Functions project " +
                $"('{CliConfigurationPathsOptions.ProjectConfigDisplayPath}' is present). " +
                "Pass --force to re-initialize.");
            return null;
        }

        if (complete && request.UnmatchedTokens.Count > 0)
        {
            RejectExistingProjectTokens(request);
            return null;
        }

        if (settings.IsAdoptable || complete)
        {
            LanguageDescriptor? detected = await DetectProjectLanguageAsync(directory, stack, cancellationToken);
            if (language is not null && detected is not null && detected != language)
            {
                throw new ProjectStackResolutionException(
                    $"--language '{explicitLanguage}' conflicts with detected project language " +
                    $"'{detected.LanguageIdentifier}' for stack '{stack.Descriptor.Identifier}'.");
            }

            language ??= detected;
        }

        language ??= await SelectLanguageAsync(stack, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        InitMode mode = complete ? InitMode.Complete : settings.IsAdoptable ? InitMode.Adopt : InitMode.Create;
        return new InitResolution(initializer, language, mode);
    }

    /// <summary>
    /// Creates the project folder and, with <c>--force</c>, clears it after confirmation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the folder is ready; otherwise <see langword="false"/> after an error or hint.
    /// </returns>
    public async Task<bool> PrepareDestinationAsync(InitContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        DirectoryInfo directory = context.WorkingDirectory.Info;
        try
        {
            context.WorkingDirectory.CreateIfNotExists();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _interaction.WriteError($"Couldn't create the project folder '{directory.FullName}': {ex.Message}");
            _interaction.WriteHint("Nothing was changed. Fix the problem, then run the same command again.");
            return false;
        }

        if (!context.Force)
        {
            return true;
        }

        if (!await ConfirmClearDirectoryAsync(directory, cancellationToken))
        {
            _interaction.WriteHint("Init cancelled. The directory was not modified.");
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ClearDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _interaction.WriteError($"Couldn't clear the project folder '{directory.FullName}': {ex.Message}");
            _interaction.WriteHint(
                "Some files may already be deleted. Fix the problem (for example, close programs that use files in "
                + "the folder), then run the same command again.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Writes <c>.func/config.json</c> for a new project before a route creates any project content.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the config was written; otherwise <see langword="false"/> after an error that says
    /// no project files were created.
    /// </returns>
    public async Task<bool> SaveProjectConfigAsync(InitRequest request,
        InitResolution resolution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolution);
        DirectoryInfo directory = request.WorkingDirectory.Info;
        try
        {
            await Configuration.CreateAsync(directory, resolution.Stack, resolution.Language, cancellationToken);
        }
        catch (Exception ex)
            when (ex is ProjectStackConfigurationException or IOException or UnauthorizedAccessException)
        {
            // The wrapper repeats the config path, which the message already names; keep only the cause.
            string reason = ex is ProjectStackConfigurationException { InnerException: { } inner }
                ? inner.Message
                : ex.Message;
            _interaction.WriteError(
                $"Couldn't save '{CliConfigurationPathsOptions.ProjectConfigDisplayPath}': {reason.TrimEnd('.')}.");
            _interaction.WriteHint(request.Force
                ? "The folder was cleared, but no project files were created. Fix the problem, then run the same "
                    + "command again."
                : "No project files were created. Fix the problem, then run the same command again.");
            return false;
        }

        WriteConfigWritten();
        return true;
    }

    /// <summary>
    /// Reports a project creation or stack setup that failed after <c>.func/config.json</c> was written, and explains
    /// how to start over with <c>--force</c>.
    /// </summary>
    /// <param name="request">The request that failed.</param>
    /// <param name="failure">What failed, including the underlying reason.</param>
    /// <returns>The exit code, 1.</returns>
    public int ReportCreationFailed(InitRequest request, string failure)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(failure);

        _interaction.WriteError(failure);

        // Raw output keeps the path intact; the original command is not echoed because no quoting protects user
        // input in both cmd and PowerShell.
        _interaction.WriteRawLine(
            $"The project folder may now hold a partly created project: {request.WorkingDirectory.Info.FullName}");
        string rerun = request.Force
            ? "run your original func init command again"
            : "run your original func init command again with --force added";
        _interaction.WriteHint($"To start over, {rerun}. --force deletes everything in the folder except .git.");
        return 1;
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is a failure that stack-owned project creation or setup is expected to
    /// throw: a file system error, or a <see cref="GracefulException"/> for a user-facing failure such as a failed
    /// <c>dotnet new</c>. Anything else is a bug and goes to the CLI's unexpected-error handling.
    /// </summary>
    public static bool IsExpectedStackFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or GracefulException;

    public void WriteConfigWritten()
        => _interaction.WriteLine(l => l
            .Muted("· Wrote ")
            .Code(CliConfigurationPathsOptions.ProjectConfigDisplayPath)
            .Muted("."));

    public void WriteSuccess(string action, IProjectStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l.Success("✓ ").Muted(action).Code(stack.Descriptor.DisplayName).Muted("."));
    }

    public async Task<int> ReportInitializedAsync(DirectoryInfo projectDirectory,
        IProjectStack stack, CancellationToken cancellationToken)
    {
        WriteSuccess("Project initialized for ", stack);
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

    private int RejectExistingProjectTokens(InitRequest request)
    {
        // Parse against the stack options only to name a stack option, including its inline '--option=value' form;
        // nothing is bound for an existing project.
        ParseResult stackParse = request.StackOptionsCommand.Parse(
            ["init", .. request.UnmatchedTokens], TemplateCommand.TokenParserConfiguration);
        Token? first = stackParse.Tokens.SkipWhile(token => token.Type == TokenType.Command).FirstOrDefault();
        if (first?.Type != TokenType.Option)
        {
            return RejectUnmatchedTokens(request.UnmatchedTokens);
        }

        _interaction.WriteError(
            $"Option '{first.Value}' is not supported for an existing project because no project content is created. " +
            "Remove it, or pass --force to re-initialize.");
        return 1;
    }

    private async Task<IProjectInitializer?> SelectInitializerAsync(string? requested,
        ProjectStackSettings settings, CancellationToken cancellationToken)
    {
        // Check the installed inventory independently of this command's capabilities. An initializer must not hide
        // a second package claiming its ID.
        IReadOnlyList<IProjectStack> installed = _catalog.Stacks;
        Dictionary<string, IProjectInitializer> eligible = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (IProjectInitializer initializer in Initializers)
        {
            string id = initializer.Stack.Descriptor.Identifier;
            if (!eligible.TryAdd(id, initializer))
            {
                throw new ProjectStackResolutionException($"Multiple installed initializers claim the '{id}' stack.");
            }

            if (!installed.Any(owner => ReferenceEquals(owner, initializer.Stack)))
            {
                throw new ProjectStackResolutionException(
                    $"The initializer for '{id}' does not reference its registered project stack.");
            }
        }

        IProjectStack? selected = null;
        if (requested is not null)
        {
            selected = _catalog.FindStack(requested);
            if (selected is null)
            {
                RenderHint(WorkloadHintKind.NoMatchingStack, requested);
                return null;
            }
        }

        selected = _catalog.ResolveProjectStack(settings, selected);
        if (selected is not null)
        {
            if (eligible.TryGetValue(selected.Descriptor.Identifier, out IProjectInitializer? initializer))
            {
                return initializer;
            }

            throw new ProjectStackResolutionException(
                $"Stack '{selected.Descriptor.Identifier}' does not provide an initializer for 'func init'.");
        }

        if (eligible.Count == 0)
        {
            RenderHint(WorkloadHintKind.NoWorkloadsInstalled);
            return null;
        }

        // Writing the wrong stack into an existing project is worse than asking, so adoption never auto-selects.
        if (!settings.IsAdoptable && Initializers is [var sole])
        {
            RenderHint(WorkloadHintKind.AutoSelectedSoleWorkload, sole.Stack.Descriptor.Identifier);
            return sole;
        }

        if (!_interaction.IsInteractive)
        {
            if (settings.IsAdoptable)
            {
                _interaction.WriteError(
                    "Couldn't detect the project's stack from local.settings.json. " +
                    "Re-run with --stack <id> to select the stack to adopt.");
            }
            else
            {
                RenderHint(WorkloadHintKind.AmbiguousStackChoice);
            }

            return null;
        }

        List<StackDescriptor> choices = [.. Initializers.Select(i => i.Stack.Descriptor)];
        StackDescriptor picked = await _interaction.PromptForSelectionAsync(
            settings.IsAdoptable ? "Adopting an existing project. Which stack is it?" : "Select a stack:",
            choices,
            d => choices.Count(c => c.DisplayName.Equals(d.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1
                ? $"{d.DisplayName} ({d.Identifier})"
                : d.DisplayName,
            cancellationToken);
        return eligible[picked.Identifier];
    }

    private async Task<LanguageDescriptor?> DetectProjectLanguageAsync(WorkingDirectory directory,
        IProjectStack stack, CancellationToken cancellationToken)
    {
        ProjectResolutionResult result = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(directory, stack), cancellationToken);
        if (result is ProjectResolutionResult.NotResolved notResolved)
        {
            return notResolved.IsUnrecognized ? null : throw new ProjectStackResolutionException(notResolved.Message);
        }

        FunctionsProject project = ((ProjectResolutionResult.Resolved)result).Project;
        if (project.Stack.Descriptor != stack.Descriptor)
        {
            throw new ProjectStackResolutionException(
                $"Selected stack '{stack.Descriptor.Identifier}' conflicts with detected project stack " +
                $"'{project.Stack.Descriptor.Identifier}'.");
        }

        return project.Language is not { } language
            ? null
            : stack.SupportedLanguages.SingleOrDefault(l => l == language)
                ?? throw new ProjectStackResolutionException(
                    $"Detected project language '{language.LanguageIdentifier}' is not supported by stack " +
                    $"'{stack.Descriptor.Identifier}'.");
    }

    private async Task<LanguageDescriptor> SelectLanguageAsync(IProjectStack stack, CancellationToken cancellationToken)
    {
        IReadOnlyList<LanguageDescriptor> choices = stack.SupportedLanguages;
        if (choices is [var only])
        {
            return only;
        }

        if (!_interaction.IsInteractive)
        {
            throw new ProjectStackResolutionException(
                $"The '{stack.Descriptor.Identifier}' stack supports multiple languages. " +
                $"Re-run with --language <{string.Join("|", choices.Select(l => l.LanguageIdentifier))}>.");
        }

        return await _interaction.PromptForSelectionAsync(
            "Select a language:",
            choices,
            l => choices.Count(c => c.DisplayName.Equals(l.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1
                ? $"{l.DisplayName} ({l.LanguageIdentifier})"
                : l.DisplayName,
            cancellationToken);
    }

    private void RenderHint(WorkloadHintKind kind, string? requested = null)
        => _hintRenderer.Render(new WorkloadHint(
            kind,
            "initialize a project",
            requested,
            [.. Initializers.Select(i => i.Stack.Descriptor.Identifier)]));

    // A fresh scaffold may pin a preview/experimental bundle channel (via `-c`).
    // Warn when no bundle workload for that channel is installed, pointing at
    // workload search so `func start` doesn't later fail to resolve the bundle.
    private async Task WarnIfBundleChannelNotInstalledAsync(DirectoryInfo projectDirectory,
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
    private async Task<bool> ConfirmClearDirectoryAsync(DirectoryInfo workingDirectory,
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
}
