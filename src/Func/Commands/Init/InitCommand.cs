// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Init;
using Azure.Functions.Cli.Commands.Templating;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Initializes an Azure Functions project. Routes to <see cref="TemplatedInitHandler"/>, which creates projects from
/// installed project templates and adopts or completes existing projects, or to <see cref="LegacyInitCommand"/>,
/// which runs the stack's existing scaffolding when no project template is installed for the language.
/// Each registered initializer may also contribute options used by its existing scaffolding
/// (e.g. dotnet's <c>--target-framework</c>).
/// </summary>
/// <remarks>
/// <para>
/// Parsing is route-neutral: the command line is parsed only for the built-in init options and the
/// project path. Tokens after them are interpreted by the selected route: the template flow binds them
/// against the selected template, and the legacy flow parses them against the typed stack options. The
/// project path is therefore given before template and stack options: <c>func init &lt;path&gt; [options]</c>.
/// </para>
/// <para>
/// <c>init</c> adopts a project with <c>host.json</c> but no CLI config, or fills in a missing language for a
/// multi-language stack. <c>--force</c> skips adoption and repair and reinitializes the directory instead.
/// </para>
/// </remarks>
internal class InitCommand : TemplateCommand, IBuiltInCommand
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
        Description = "Re-initialize the directory: deletes its contents (except a .git directory) " +
            "before scaffolding the new project."
    };

    // Stack options are kept off this command so their typed validation cannot reject a template invocation before
    // routing; only the routes that consume them parse against this command.
    private readonly Command _stackOptionsCommand = new("init");
    private readonly IReadOnlyList<IProjectInitializer> _initializers;
    private readonly InitWorkflow _workflow;
    private readonly TemplatedInitHandler _templatedInit;
    private readonly LegacyInitCommand _legacyInit;

    public InitCommand(
        IInteractionService interaction,
        IWorkloadHintRenderer hintRenderer,
        ILocalSettingsProvider localSettingsProvider,
        IFunctionsProjectResolver projectResolver,
        IHostJsonBundleSectionReader bundleSectionReader,
        InstalledBundleScanner bundleScanner,
        ITemplaterFactory templaterFactory,
        IEnumerable<IProjectInitializer> initializers)
        : base("init", "Initialize a new Azure Functions project.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(hintRenderer);
        ArgumentNullException.ThrowIfNull(localSettingsProvider);
        ArgumentNullException.ThrowIfNull(projectResolver);
        ArgumentNullException.ThrowIfNull(bundleSectionReader);
        ArgumentNullException.ThrowIfNull(bundleScanner);
        ArgumentNullException.ThrowIfNull(templaterFactory);
        ArgumentNullException.ThrowIfNull(initializers);

        _initializers = initializers.ToList();
        _workflow = new InitWorkflow(
            interaction,
            hintRenderer,
            localSettingsProvider,
            projectResolver,
            bundleSectionReader,
            bundleScanner,
            _initializers);
        _templatedInit = new TemplatedInitHandler(_workflow, templaterFactory);
        _legacyInit = new LegacyInitCommand(_workflow);

        StackOption.Description = BuildStackOptionDescription(_initializers);
        LanguageOption.Description = BuildLanguageOptionDescription(_initializers);

        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        // The registry shares canonical instances across workloads; help uses
        // their ownership to distinguish shared options from stack-specific ones.
        InitOptionRegistry registry = new(_stackOptionsCommand);
        foreach (IProjectInitializer initializer in _initializers)
        {
            registry.SetActiveStack(initializer.Stack);
            IReadOnlyList<Option> options = initializer.GetInitOptions(registry);
            // Bundle options stay general even when only one bundle-using stack is installed.
            RegisterStackOptionHelp(initializer.Stack, initializer.DisplayName,
                options.Where(option => option.Name != CommonInitOptions.NoBundleOptionName
                    && option.Name != CommonInitOptions.BundlesChannelOptionName));
        }
    }

    protected override string HelpFooterHint =>
        "Specify the project path before template or stack options: `func init <path> [template options]`. " +
        "Looking for more stacks? Run `func workload search --stack` to list installable stack workloads.";

    internal override IEnumerable<Option> GetHelpOptions() => Options.Concat(_stackOptionsCommand.Options);

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

        InitRequest request = CreateRequest(parseResult);
        InitializationState state = InitWorkflow.DetectInitializationState(request.WorkingDirectory.Info);
        string? existingRuntime = null;

        // Heal one specific partial state: config exists with a runtime but no
        // language on a multi-language stack. Detection here happens before the
        // FullyInitialized refuse so the user doesn't have to reach for --force
        // (which would wipe profiles, custom keys, etc.) just to fill in a
        // missing language field. Other partial shapes still require --force.
        if (state == InitializationState.FullyInitialized
            && !request.Force
            && InitWorkflow.TryReadExistingStack(request.WorkingDirectory.Info) is { } snapshot
            && !string.IsNullOrWhiteSpace(snapshot.Runtime)
            && string.IsNullOrWhiteSpace(snapshot.Language)
            && _workflow.FindInitializerForCandidate(snapshot.Runtime) is { SupportedLanguages.Count: > 1 })
        {
            state = InitializationState.PartiallyInitialized;
            existingRuntime = snapshot.Runtime;
        }

        // .func/config.json already present means the project has been fully
        // initialized by v5; re-running without --force would silently no-op
        // the config write and re-scaffold on top of the user's code.
        if (state == InitializationState.FullyInitialized && !request.Force)
        {
            _workflow.Interaction.WriteError(
                "This directory already contains a Functions project " +
                $"('{CliConfigurationPathsOptions.ProjectConfigDisplayPath}' is present). " +
                "Pass --force to re-initialize.");
            return 1;
        }

        // host.json without .func/config.json means a pre-existing Functions
        // project that predates the v5 CLI config. Adopt it without creating
        // content. --force still means "wipe and re-create".
        if (!request.Force
            && state is InitializationState.AdoptableExistingProject or InitializationState.PartiallyInitialized)
        {
            return await _templatedInit.ReconcileExistingAsync(request, state, existingRuntime, cancellationToken);
        }

        IProjectInitializer? initializer = await _workflow.SelectInitializerAsync(
            request.RequestedStack, cancellationToken);
        if (initializer is null)
        {
            return 1;
        }

        (string? language, int? errorCode) = await _workflow.ResolveLanguageAsync(
            request.Language, initializer, cancellationToken);
        if (errorCode is int code)
        {
            return code;
        }

        if (!string.IsNullOrWhiteSpace(language)
            && await _templatedInit.TryCreateAsync(request, initializer, language, cancellationToken)
                is int templateExitCode)
        {
            return templateExitCode;
        }

        return await _legacyInit.CreateAsync(request, initializer, language, cancellationToken);
    }

    private InitRequest CreateRequest(ParseResult parseResult)
    {
        string? language = parseResult.GetValue(LanguageOption);
        if (!string.IsNullOrWhiteSpace(language))
        {
            language = language.Trim().ToLowerInvariant();
        }

        return new InitRequest(
            parseResult.GetValue(PathArgument!)!,
            parseResult.GetValue(NameOption),
            parseResult.GetValue(StackOption),
            language,
            parseResult.GetValue(ForceOption),
            _stackOptionsCommand,
            GetReservedOptions([]),
            GetTemplateTokens(parseResult, []));
    }

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
}
