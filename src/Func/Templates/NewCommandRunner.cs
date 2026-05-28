// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Orchestrator for the <c>func new</c> pipeline. Lays out the resolution →
/// selection → hydration → apply flow; the engine-dependent steps (channel
/// match, min-bundle gate, engine dispatch) land once
/// <see cref="ITemplateEngineProvider"/> implementations are registered.
/// </summary>
/// <remarks>
/// This scaffold wires the always-available steps (profile / project / stack /
/// language resolution) through real services so the failure modes that don't
/// depend on an engine — no project, no <c>stack.runtime</c>, no
/// <c>stack.language</c> on a multi-language stack, no installed templates
/// workload — are already observable end-to-end. The engine-dispatch terminal
/// returns a "no engines registered" hint until engines arrive.
/// </remarks>
internal sealed class NewCommandRunner
{
    private readonly IInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly IProfileResolver _profileResolver;
    private readonly IOptionsMonitor<StackOptions> _stackOptions;
    private readonly IReadOnlyDictionary<string, IProjectInitializer> _projectInitializersByStack;
    private readonly IInstalledTemplatesWorkloads _installedTemplatesWorkloads;
    private readonly ITemplateEngineProviderRegistry _engineProviders;
    private readonly TemplateOptionHydrator _optionHydrator;
    private readonly TemplatePicker _picker;
    private readonly NewCommandRenderer _renderer;

    public NewCommandRunner(
        IInteractionService interaction,
        IFunctionsProjectResolver projectResolver,
        IProfileResolver profileResolver,
        IOptionsMonitor<StackOptions> stackOptions,
        IEnumerable<IProjectInitializer> projectInitializers,
        IInstalledTemplatesWorkloads installedTemplatesWorkloads,
        ITemplateEngineProviderRegistry engineProviders,
        TemplateOptionHydrator optionHydrator,
        TemplatePicker picker,
        NewCommandRenderer renderer)
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _stackOptions = stackOptions ?? throw new ArgumentNullException(nameof(stackOptions));
        ArgumentNullException.ThrowIfNull(projectInitializers);
        _installedTemplatesWorkloads = installedTemplatesWorkloads ?? throw new ArgumentNullException(nameof(installedTemplatesWorkloads));
        _engineProviders = engineProviders ?? throw new ArgumentNullException(nameof(engineProviders));
        _optionHydrator = optionHydrator ?? throw new ArgumentNullException(nameof(optionHydrator));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));

        _projectInitializersByStack = projectInitializers
            .Where(p => !string.IsNullOrWhiteSpace(p.Stack))
            .GroupBy(p => p.Stack.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Drives the <c>func new</c> pipeline. Currently terminates at step 6
    /// ("ListTemplates") with the "no engines registered" hint until engine
    /// providers exist; later PRs replace the terminal return with the
    /// stage-A → stage-B → ApplyAsync flow.
    /// </summary>
    public async Task<int> ExecuteAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        // Step 1 + 2: resolve profile and project. ProfileResolver swallows
        // its own diagnostics; project failure is a hard exit.
        var profileContext = new ProfileResolutionContext(
            invocation.WorkingDirectory.Info,
            RequestedProfileName: null,
            CanPrompt: _interaction.IsInteractive);
        await _profileResolver.ResolveAsync(profileContext, cancellationToken);

        ProjectResolutionResult projectResult = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(invocation.WorkingDirectory),
            cancellationToken);

        if (projectResult is not ProjectResolutionResult.Resolved resolved)
        {
            string message = projectResult switch
            {
                ProjectResolutionResult.NotResolved nr => nr.Message,
                _ => "Failed to resolve a Functions project.",
            };
            _interaction.WriteError(message);
            _interaction.WriteLine(l => l
                .Muted("Run ")
                .Code("func init")
                .Muted(" to scaffold a project before adding functions."));
            return 1;
        }

        // Step 4: look up installed templates workload rows for the
        // active stack. Without engine providers we cannot actually
        // list templates, but surfacing "no templates workload installed"
        // against a real registry exercises the integration.
        string stack = resolved.Project.StackName;
        IReadOnlyList<InstalledTemplatesWorkload> installed = await _installedTemplatesWorkloads.ListInstalledAsync(
            stack, cancellationToken);
        if (installed.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(stack);
            return 1;
        }

        // Step 5: language resolution via IOptionsMonitor<StackOptions>.
        string projectDirectory = Path.GetFullPath(invocation.WorkingDirectory.Info.FullName);
        StackOptions stackOptions = _stackOptions.Get(projectDirectory);
        string? language = ResolveLanguage(stack, stackOptions);
        if (language is null)
        {
            _renderer.RenderMissingLanguage(stack, projectDirectory);
            return 1;
        }

        // Step 6: aggregate templates across every registered engine
        // provider for the active stack. Terminates with the "no engines
        // registered" hint when no providers are wired.
        if (_engineProviders.Providers.Count == 0)
        {
            _renderer.RenderNoEnginesRegistered();
            return 1;
        }

        var listContext = new TemplateListContext(invocation.WorkingDirectory, stack, language);
        List<FunctionTemplateInfo> templates = [];
        foreach (ITemplateEngineProvider provider in _engineProviders.Providers)
        {
            IReadOnlyList<FunctionTemplateInfo> contributed = await provider.ListTemplatesAsync(listContext, cancellationToken);
            templates.AddRange(contributed);
        }

        if (templates.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(stack);
            return 1;
        }

        // Steps 7-12 follow in a later commit: stage-A template
        // resolution, stage-B hydration + re-parse, function-name
        // resolution, bundle gates, and engine dispatch.
        _ = invocation;
        _ = _optionHydrator;
        _ = _picker;
        _interaction.WriteHint(
            $"`func new` scaffold flow lands in a follow-up PR. {templates.Count} template(s) discovered.");
        return 1;
    }

    /// <summary>
    /// Lists templates for <c>func new --list</c>. Same resolution
    /// gates as <see cref="ExecuteAsync"/> minus the stage-B / apply tail.
    /// </summary>
    public async Task<int> ListAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        ProjectResolutionResult projectResult = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(invocation.WorkingDirectory),
            cancellationToken);

        if (projectResult is not ProjectResolutionResult.Resolved resolved)
        {
            _renderer.RenderProjectRequired();
            return 1;
        }

        string stack = resolved.Project.StackName;
        string projectDirectory = Path.GetFullPath(invocation.WorkingDirectory.Info.FullName);
        StackOptions stackOptions = _stackOptions.Get(projectDirectory);
        string? language = ResolveLanguage(stack, stackOptions);
        if (language is null)
        {
            _renderer.RenderMissingLanguage(stack, projectDirectory);
            return 1;
        }

        IReadOnlyList<InstalledTemplatesWorkload> installed = await _installedTemplatesWorkloads.ListInstalledAsync(
            stack, cancellationToken);
        if (installed.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(stack);
            return 1;
        }

        if (_engineProviders.Providers.Count == 0)
        {
            _renderer.RenderNoEnginesRegistered();
            return 1;
        }

        var listContext = new TemplateListContext(invocation.WorkingDirectory, stack, language);
        List<FunctionTemplateInfo> templates = [];
        foreach (ITemplateEngineProvider provider in _engineProviders.Providers)
        {
            templates.AddRange(await provider.ListTemplatesAsync(listContext, cancellationToken));
        }

        _renderer.RenderCatalogue(stack, language, templates);
        return 0;
    }

    /// <summary>
    /// Language resolution: read <c>StackOptions.Language</c>, fall back to
    /// the stack's single canonical language for single-language stacks,
    /// return <c>null</c> for multi-language stacks when the configured
    /// language is missing (the runner treats <c>null</c> as a hard error
    /// and points at <c>func init</c>).
    /// </summary>
    private string? ResolveLanguage(string stack, StackOptions stackOptions)
    {
        if (!string.IsNullOrWhiteSpace(stackOptions.Language))
        {
            return stackOptions.Language.Trim();
        }

        if (_projectInitializersByStack.TryGetValue(stack, out IProjectInitializer? initializer)
            && initializer.SupportedLanguages.Count == 1)
        {
            return initializer.SupportedLanguages[0];
        }

        return null;
    }
}

/// <summary>
/// Bundled invocation context: only the values the runner needs from the
/// SCL parse, decoupled from <c>NewCommand</c>'s argument graph so tests
/// can construct an invocation directly.
/// </summary>
internal sealed record NewInvocation(
    WorkingDirectory WorkingDirectory,
    string? RequestedTemplate,
    string? RequestedFunctionName,
    bool Force,
    bool NonInteractive);
