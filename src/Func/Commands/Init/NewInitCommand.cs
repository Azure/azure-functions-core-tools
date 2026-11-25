// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using Azure.Functions.Cli.Commands.Templating;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Commands.Init;

/// <summary>
/// The template-based <c>func init</c> flow: creates projects from the installed <c>func-{stack}</c> project template,
/// and adopts or completes existing projects.
/// </summary>
/// <remarks>
/// Project content is owned by the template, which also owns the meaning of every option after the project path. The
/// selected stack contributes only non-scaffolding setup through
/// <see cref="IProjectInitializer.ConfigureProjectAsync"/>, and stack init options are never parsed in this flow.
/// </remarks>
internal sealed class NewInitCommand(InitWorkflow workflow, ITemplaterFactory templaterFactory)
{
    private readonly InitWorkflow _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    private readonly ITemplaterFactory _templaterFactory = templaterFactory
        ?? throw new ArgumentNullException(nameof(templaterFactory));

    /// <summary>
    /// Adopts a project that predates <c>.func/config.json</c>, or completes a config missing its language, without
    /// creating project content.
    /// </summary>
    public async Task<int> ReconcileExistingAsync(
        InitRequest request,
        InitializationState state,
        string? existingRuntime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool heal = state switch
        {
            InitializationState.PartiallyInitialized => true,
            InitializationState.AdoptableExistingProject => false,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

        // No template or stack scaffolding runs for an existing project, so options only they consume are rejected
        // before any prompt or write.
        if (request.UnmatchedTokens.Count > 0)
        {
            return RejectExistingProjectTokens(request);
        }

        // Where the project's stack runtime comes from: heal trusts what's
        // already in .func/config.json; adopt reads local.settings.json
        // (FUNCTIONS_WORKER_RUNTIME).
        string? projectRuntime = heal
            ? existingRuntime
            : _workflow.ResolveLegacyWorkerRuntime(request.WorkingDirectory.Info);
        string? requestedStack = request.RequestedStack;

        // Resolve both signals to a concrete initializer (or null) using
        // alias-aware matching, so e.g. "dotnet-isolated" in
        // local.settings.json maps to the dotnet initializer and
        // `--stack dotnet` is recognized as agreeing with it.
        IProjectInitializer? requested = !string.IsNullOrWhiteSpace(requestedStack)
            ? _workflow.FindInitializerForCandidate(requestedStack)
            : null;
        IProjectInitializer? declared = projectRuntime is not null
            ? _workflow.FindInitializerForCandidate(projectRuntime)
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
            _workflow.Interaction.WriteError(
                $"--stack '{requestedStack}' conflicts with the project's runtime '{projectRuntime}'. " +
                "Re-run with the matching stack, or pass --force to override.");
            return 1;
        }

        // Pick a candidate: explicit --stack wins, else snap to the
        // project's declared runtime.
        string? candidate = !string.IsNullOrWhiteSpace(requestedStack) ? requestedStack : projectRuntime;
        IProjectInitializer? initializer = requested ?? declared;

        if (candidate is not null && initializer is null)
        {
            // Candidate isn't an installed stack (and isn't an alias of
            // one). Refuse rather than write a config we can't validate.
            // The hint uses a `<stack>` placeholder on purpose: the
            // candidate string may itself be unknown (e.g. a typo or a
            // runtime value with no installed alias owner) and echoing
            // it would promise a `func setup` command that won't
            // necessarily work.
            _workflow.Interaction.WriteError(
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
            initializer = await _workflow.PromptForAdoptionStackAsync(cancellationToken);
            if (initializer is null)
            {
                return 1;
            }
        }

        // Reuse the runtime project resolver so adoption/heal doesn't need its
        // own detection rules.
        string? language = request.Language;
        if (string.IsNullOrWhiteSpace(language))
        {
            language = await _workflow.DetectAdoptedLanguageAsync(request.WorkingDirectory, cancellationToken);
        }

        // Adopt without a resolvable language: skip prompting, the user isn't
        // changing existing code. Heal is multi-language by construction, so
        // language must be resolved (either from --language or the detector
        // above); fall through to ResolveLanguageAsync to surface the right
        // error if it wasn't.
        if (heal || !string.IsNullOrWhiteSpace(language))
        {
            (string? resolved, int? errorCode) = await _workflow.ResolveLanguageAsync(
                language, initializer, cancellationToken);
            if (errorCode is int code)
            {
                return code;
            }

            language = resolved;
        }

        string? persistedLanguage = InitWorkflow.GetPersistedLanguage(initializer, language);
        if (heal)
        {
            _workflow.MergeLanguageIntoConfig(request.WorkingDirectory.Info, persistedLanguage);
            _workflow.Interaction.WriteBlankLine();
            _workflow.Interaction.WriteLine(l => l
                .Success("✓ ")
                .Muted("Existing project completed for ")
                .Code(initializer.Stack)
                .Muted("."));
            return 0;
        }

        _workflow.WriteCliConfigurationFile(
            request.WorkingDirectory.Info, initializer.Stack, persistedLanguage, request.Force);
        _workflow.Interaction.WriteBlankLine();
        _workflow.Interaction.WriteLine(l => l
            .Success("✓ ")
            .Muted("Existing project adopted for ")
            .Code(initializer.Stack)
            .Muted("."));
        return 0;
    }

    /// <summary>
    /// Creates the project from the installed <c>func-{stack}</c> project template for <paramref name="language"/>.
    /// </summary>
    /// <returns>
    /// The exit code, or <see langword="null"/> when no project template is installed for <paramref name="language"/>.
    /// </returns>
    public async Task<int?> TryCreateAsync(
        InitRequest request,
        IProjectInitializer initializer,
        string language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(initializer);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        using Templater templater = _templaterFactory.Create(new TemplateEngineContext(
            request.WorkingDirectory,
            new TemplateEngineProjectContext(request.WorkingDirectory, initializer.Stack, language)));
        IReadOnlyList<FuncTemplateInfo> candidates;
        try
        {
            candidates = (await templater.ListProjectTemplatesAsync(language, cancellationToken)).Templates;
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or JsonException or IOException or UnauthorizedAccessException)
        {
            _workflow.Interaction.WriteError(ex.Message);
            return 1;
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        // Once project templates exist for the language, failures never fall back to stack scaffolding.
        string defaultShortName = $"func-{initializer.Stack}";
        FuncTemplateInfo[] defaults = [.. candidates
            .Where(template => template.ShortNameList.Contains(defaultShortName, StringComparer.OrdinalIgnoreCase))];
        if (defaults.Length != 1)
        {
            string identities = string.Join(", ", candidates.Select(template => template.Identity));
            _workflow.Interaction.WriteError(
                $"Expected one default project template named '{defaultShortName}' for language '{language}', " +
                $"but found {defaults.Length}. Matching templates: {identities}.");
            return 1;
        }

        FuncTemplateInfo template = defaults[0];
        IReadOnlyDictionary<string, string> parameters;
        try
        {
            parameters = TemplateArgumentBinder.Bind(
                template,
                request.UnmatchedTokens,
                request.ReservedOptions,
                out IReadOnlyList<string> unrecognizedTokens);
            if (unrecognizedTokens.Count > 0)
            {
                return _workflow.RejectUnmatchedTokens(unrecognizedTokens);
            }
        }
        catch (InvalidOperationException ex)
        {
            _workflow.Interaction.WriteError(ex.Message);
            return 1;
        }

        InitContext context = new(request.WorkingDirectory, request.ProjectName, language, request.Force);
        string outputPath = request.WorkingDirectory.Info.FullName;
        ITemplateCreationResult preflight = await template.InstantiateAsync(
            context.ProjectName, outputPath, parameters, context.Force, dryRun: true, cancellationToken);
        if ((preflight.Status != CreationResultStatus.Success
                && !(context.Force && preflight.Status == CreationResultStatus.DestructiveChangesDetected))
            || (!context.Force && HasExistingFileChanges(preflight)))
        {
            _workflow.Interaction.WriteError(DescribeCreationFailure(template, preflight));
            return 1;
        }

        if (!await _workflow.PrepareDestinationAsync(context, cancellationToken))
        {
            return 1;
        }

        ITemplateCreationResult result = await template.InstantiateAsync(
            context.ProjectName, outputPath, parameters, context.Force, dryRun: false, cancellationToken);
        if (result.Status != CreationResultStatus.Success)
        {
            _workflow.Interaction.WriteError(DescribeCreationFailure(template, result));
            return 1;
        }

        _workflow.WriteCliConfigurationFile(
            request.WorkingDirectory.Info,
            initializer.Stack,
            InitWorkflow.GetPersistedLanguage(initializer, language),
            request.Force);

        await initializer.ConfigureProjectAsync(context, cancellationToken);

        // Stack setup may observe cancellation and return normally; a cancelled init never reports success.
        cancellationToken.ThrowIfCancellationRequested();

        // POST-ACTION INTEGRATION DEFERRED TO #5963: template post-actions run here, after stack setup and before
        // success is reported.

        _workflow.Interaction.WriteBlankLine();
        return await _workflow.ReportInitializedAsync(
            request.WorkingDirectory.Info, initializer.Stack, cancellationToken);
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
            return _workflow.RejectUnmatchedTokens(request.UnmatchedTokens);
        }

        _workflow.Interaction.WriteError(
            $"Option '{first.Value}' is not supported for an existing project because no project content is created. " +
            "Remove it, or pass --force to re-initialize.");
        return 1;
    }

    private static string DescribeCreationFailure(FuncTemplateInfo template, ITemplateCreationResult result)
    {
        if (HasExistingFileChanges(result) || result.Status == CreationResultStatus.DestructiveChangesDetected)
        {
            string files = string.Join(", ", result.CreationEffects?.FileChanges
                .Where(change => change.ChangeKind != ChangeKind.Create)
                .Select(change => change.TargetRelativePath) ?? []);
            return $"Cannot initialize the project because these files already exist: {files}. "
                + "Use --force to replace the directory contents.";
        }

        return $"Failed to initialize the project from template '{template.Identity}': "
            + (result.ErrorMessage ?? result.Status.ToString());
    }

    private static bool HasExistingFileChanges(ITemplateCreationResult result)
        => result.CreationEffects?.FileChanges.Any(change => change.ChangeKind != ChangeKind.Create) == true;
}
