// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using System.Diagnostics;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Profiles;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;
using Azure.Functions.Cli.Templates.Search;
using Microsoft.Extensions.Options;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Orchestrator for the <c>func new</c> pipeline. Resolves the profile /
/// project / stack / language context and the project's extension bundle,
/// then hands off to the in-process Microsoft templating engine
/// (<see cref="IFuncTemplateCatalog"/> for listing / hydration,
/// <see cref="IFuncTemplateScaffolder"/> for scaffolding) and to
/// <see cref="IFuncTemplatePackageService"/> for template-package lifecycle
/// operations.
/// </summary>
internal sealed class NewCommandRunner
{
    private readonly IInteractionService _interaction;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly IProfileResolver _profileResolver;
    private readonly IOptionsMonitor<StackOptions> _stackOptions;
    private readonly IReadOnlyDictionary<string, IProjectInitializer> _projectInitializersByStack;
    private readonly TemplateOptionHydrator _optionHydrator;
    private readonly TemplatePicker _picker;
    private readonly NewCommandRenderer _renderer;
    private readonly IFuncTemplateCatalog _catalog;
    private readonly IFuncTemplateScaffolder _scaffolder;
    private readonly IFuncTemplatePackageService _packageService;
    private readonly IFuncExtensionBundleContextAccessor _bundleContextAccessor;
    private readonly IHostJsonBundleSectionReader _hostJsonReader;
    private readonly IExtensionBundleResolver _bundleResolver;
    private readonly IFuncTemplateSearchService _searchService;

    public NewCommandRunner(
        IInteractionService interaction,
        IFunctionsProjectResolver projectResolver,
        IProfileResolver profileResolver,
        IOptionsMonitor<StackOptions> stackOptions,
        IEnumerable<IProjectInitializer> projectInitializers,
        TemplateOptionHydrator optionHydrator,
        TemplatePicker picker,
        NewCommandRenderer renderer,
        IFuncTemplateCatalog catalog,
        IFuncTemplateScaffolder scaffolder,
        IFuncTemplatePackageService packageService,
        IFuncExtensionBundleContextAccessor bundleContextAccessor,
        IHostJsonBundleSectionReader hostJsonReader,
        IExtensionBundleResolver bundleResolver,
        IFuncTemplateSearchService searchService)
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
        _profileResolver = profileResolver ?? throw new ArgumentNullException(nameof(profileResolver));
        _stackOptions = stackOptions ?? throw new ArgumentNullException(nameof(stackOptions));
        ArgumentNullException.ThrowIfNull(projectInitializers);
        _optionHydrator = optionHydrator ?? throw new ArgumentNullException(nameof(optionHydrator));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _scaffolder = scaffolder ?? throw new ArgumentNullException(nameof(scaffolder));
        _packageService = packageService ?? throw new ArgumentNullException(nameof(packageService));
        _bundleContextAccessor = bundleContextAccessor ?? throw new ArgumentNullException(nameof(bundleContextAccessor));
        _hostJsonReader = hostJsonReader ?? throw new ArgumentNullException(nameof(hostJsonReader));
        _bundleResolver = bundleResolver ?? throw new ArgumentNullException(nameof(bundleResolver));
        _searchService = searchService ?? throw new ArgumentNullException(nameof(searchService));

        _projectInitializersByStack = projectInitializers
            .Where(p => !string.IsNullOrWhiteSpace(p.Stack))
            .GroupBy(p => p.Stack.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<int> ExecuteAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        ResolutionOutcome outcome = await ResolveContextAsync(invocation, cancellationToken);
        if (outcome.Failure is { } failure)
        {
            RenderResolutionFailure(failure);
            return 1;
        }

        ResolvedContext resolved = outcome.Context!;

        // Step 11a: extension-bundle presence gate. A project whose declared
        // bundle cannot be resolved at all is a hard error on the scaffold
        // path — the scaffolded binding would never bind at host launch.
        BundleResolution bundle = await ApplyBundleContextAsync(resolved, invocation.WorkingDirectory, cancellationToken);
        if (bundle is BundleResolution.Unresolvable unresolvable)
        {
            _renderer.RenderMissingExtensionBundle(resolved.Stack, unresolvable.SuggestedBundleId);
            return 1;
        }

        // The constraint context is now set, so the catalog hides
        // constraint-restricted templates from the resolution set.
        IReadOnlyList<FunctionTemplateInfo> templates =
            await ListTemplatesAsync(invocation.WorkingDirectory, resolved, cancellationToken);
        if (templates.Count == 0)
        {
            _renderer.RenderNoTemplatesInstalled(resolved.Stack);
            return 1;
        }

        FunctionTemplateInfo? template = await ResolveTemplateAsync(invocation, resolved, templates, cancellationToken);
        if (template is null)
        {
            return 1;
        }

        string functionName = invocation.RequestedFunctionName
            ?? template.DefaultFunctionName
            ?? template.Id;

        var context = new NewContext(
            invocation.WorkingDirectory,
            template,
            functionName,
            resolved.Language,
            invocation.Force,
            InstallDirectory: null,
            UserOptionValues: invocation.UserOptionValues);

        ParseResult stageBParse = BuildStageBParseResult(template, invocation.UserOptionValues);
        TemplateApplicationResult result = await _scaffolder.ApplyAsync(context, stageBParse, cancellationToken);
        return RenderApplyResult(template, functionName, result, invocation.JsonOutput);
    }

    /// <summary>
    /// Lists templates for <c>func new --list</c>. Same resolution and bundle
    /// gates as <see cref="ExecuteAsync"/> minus the scaffold tail;
    /// constraint-restricted templates are hidden from the returned set.
    /// </summary>
    public async Task<int> ListAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        ResolutionOutcome outcome = await ResolveContextAsync(invocation, cancellationToken);
        if (outcome.Failure is { } failure)
        {
            RenderResolutionFailure(failure);
            return 1;
        }

        ResolvedContext resolved = outcome.Context!;

        // Listing never hard-fails on an unresolvable bundle: the constraint
        // simply drops restricted templates. Resolve to set the constraint
        // context, then list.
        await ApplyBundleContextAsync(resolved, invocation.WorkingDirectory, cancellationToken);

        IReadOnlyList<FunctionTemplateInfo> templates =
            await ListTemplatesAsync(invocation.WorkingDirectory, resolved, cancellationToken);
        if (invocation.JsonOutput)
        {
            _renderer.RenderCatalogueJson(resolved.Stack, resolved.Language, templates);
        }
        else
        {
            _renderer.RenderCatalogue(resolved.Stack, resolved.Language, templates);
        }

        return 0;
    }

    /// <summary>
    /// Installs a template package into the func hive. Bypasses the project /
    /// profile gates (D30) so it works in an empty directory.
    /// </summary>
    public async Task<int> InstallPackageAsync(TemplatePackageInstallRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TemplatePackageInstallResult result = await _packageService.InstallAsync(request, cancellationToken);
        _renderer.RenderInstallResult(result);
        return result is TemplatePackageInstallResult.Installed or TemplatePackageInstallResult.AlreadyInstalled
            ? 0
            : 1;
    }

    /// <summary>
    /// Uninstalls a template package from the func hive. Bypasses the project /
    /// profile gates (D30).
    /// </summary>
    public async Task<int> UninstallPackageAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        TemplatePackageUninstallResult result = await _packageService.UninstallAsync(packageId, cancellationToken);
        _renderer.RenderUninstallResult(result);

        // "Not installed" is idempotent success, mirroring `func workload uninstall`.
        return result is TemplatePackageUninstallResult.Failed ? 1 : 0;
    }

    /// <summary>
    /// Updates one or all installed template packages. Bypasses the project /
    /// profile gates (D30).
    /// </summary>
    public async Task<int> UpdatePackagesAsync(TemplatePackageUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TemplatePackageUpdateResult result = await _packageService.UpdateAsync(request, cancellationToken);
        _renderer.RenderUpdateResult(result);
        return result is TemplatePackageUpdateResult.Failed ? 1 : 0;
    }

    /// <summary>
    /// Searches for template packages by term over the func-published index
    /// (or a <c>--source</c> feed queried directly), annotates each result
    /// with its installed state, and renders the results. Bypasses the
    /// project / profile gates (D30) so search works in an empty directory.
    /// </summary>
    public async Task<int> SearchTemplatesAsync(string? term, string? source, CancellationToken cancellationToken)
    {
        FuncSearchResults results;
        try
        {
            results = await _searchService.SearchAsync(new FuncSearchRequest(term, source), cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FuncSearchIndexFormatException or InvalidOperationException)
        {
            throw new GracefulException(ex.Message, ex, isUserError: true);
        }

        _renderer.RenderSearchResults(results);
        return 0;
    }

    /// <summary>
    /// Resolves the single template identified by <paramref name="templateId"/>
    /// for the project at <paramref name="invocation"/>, then hands back the
    /// hydrated <see cref="Option"/> list the stage-B help renderer needs.
    /// Returns <c>null</c> when the project can't be resolved or
    /// <paramref name="templateId"/> doesn't match any catalogued template.
    /// </summary>
    public async Task<IReadOnlyList<Option>?> HydrateOptionsForTemplateAsync(
        NewInvocation invocation,
        string templateId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HydratedTemplateOption>? paired =
            await HydrateOptionsForTemplateWithIdsAsync(invocation, templateId, cancellationToken);

        return paired?.Select(p => p.Option).ToList();
    }

    /// <summary>
    /// Same as <see cref="HydrateOptionsForTemplateAsync"/> but also returns
    /// the prompt id each option projects from. <c>NewCommand</c> uses this
    /// overload on the execute path to map user-supplied values back to the
    /// prompt id the engine resolves against.
    /// </summary>
    /// <remarks>
    /// Pre-parse / hydration callers are best-effort: when resolution fails
    /// they return <c>null</c> silently. Rendering the failure is the job of
    /// the execute / list entry-points.
    /// </remarks>
    public async Task<IReadOnlyList<HydratedTemplateOption>?> HydrateOptionsForTemplateWithIdsAsync(
        NewInvocation invocation,
        string templateId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        ResolutionOutcome outcome = await ResolveContextAsync(invocation, cancellationToken);
        if (outcome.Context is null)
        {
            return null;
        }

        ResolvedContext resolved = outcome.Context;

        // Set the constraint context so a bundle-restricted template hydrates
        // nothing (it won't be in the catalogue) rather than surfacing options.
        await ApplyBundleContextAsync(resolved, invocation.WorkingDirectory, cancellationToken);

        IReadOnlyList<FunctionTemplateInfo> templates =
            await ListTemplatesAsync(invocation.WorkingDirectory, resolved, cancellationToken);
        FunctionTemplateInfo? template = ResolveByShortName(templates, templateId);
        if (template is null)
        {
            return null;
        }

        return _optionHydrator.HydrateWithIds(template);
    }

    private async Task<ResolutionOutcome> ResolveContextAsync(
        NewInvocation invocation,
        CancellationToken cancellationToken)
    {
        // Step 1: resolve the active profile. Diagnostics are surfaced by the
        // resolver itself.
        await _profileResolver.ResolveAsync(
            new ProfileResolutionContext(
                invocation.WorkingDirectory.Info,
                RequestedProfileName: null,
                CanPrompt: _interaction.IsInteractive),
            cancellationToken);

        // Step 2: resolve the project (hard exit if absent).
        ProjectResolutionResult projectResult = await _projectResolver.ResolveProjectAsync(
            new ProjectResolutionContext(invocation.WorkingDirectory),
            cancellationToken);

        if (projectResult is not ProjectResolutionResult.Resolved resolved)
        {
            return ResolutionOutcome.Fail(new ResolutionFailure(ResolutionFailureKind.ProjectRequired));
        }

        string stack = resolved.Project.StackName;

        // Step 3: language resolution via IOptionsMonitor<StackOptions>.
        string projectDirectory = Path.GetFullPath(invocation.WorkingDirectory.Info.FullName);
        StackOptions stackOptionsBound = _stackOptions.Get(projectDirectory);
        string? language = ResolveLanguage(stack, stackOptionsBound);
        if (language is null)
        {
            return ResolutionOutcome.Fail(new ResolutionFailure(
                ResolutionFailureKind.MissingLanguage,
                Stack: stack,
                ProjectPath: projectDirectory));
        }

        return ResolutionOutcome.Succeed(new ResolvedContext(stack, language));
    }

    /// <summary>
    /// Resolves the project's extension bundle and publishes it to the engine
    /// constraint via <see cref="IFuncExtensionBundleContextAccessor"/>. The
    /// <c>func-extension-bundle</c> constraint reads
    /// <see cref="IFuncExtensionBundleContextAccessor.Current"/> during
    /// catalog evaluation, so this must run before every list / hydrate /
    /// scaffold.
    /// </summary>
    private async Task<BundleResolution> ApplyBundleContextAsync(
        ResolvedContext resolved,
        WorkingDirectory workingDirectory,
        CancellationToken cancellationToken)
    {
        BundleResolution resolution = await ResolveBundleAsync(resolved, workingDirectory, cancellationToken);
        _bundleContextAccessor.Current = resolution is BundleResolution.Resolved r ? r.Context : null;
        return resolution;
    }

    private async Task<BundleResolution> ResolveBundleAsync(
        ResolvedContext resolved,
        WorkingDirectory workingDirectory,
        CancellationToken cancellationToken)
    {
        // DotNet carries bindings via worker-SDK package references, not an
        // extension bundle, so there is nothing to gate.
        if (string.Equals(resolved.Stack, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return BundleResolution.NotApplicable.Instance;
        }

        HostJsonBundleSection? section = await _hostJsonReader.ReadAsync(workingDirectory.Info, cancellationToken);
        if (section is null)
        {
            // No bundle declared — nothing to publish to the constraint.
            return BundleResolution.NotApplicable.Instance;
        }

        var context = new ExtensionBundleProjectContext(
            BundleId: section.Id,
            HostJsonVersionRange: section.Version,
            WorkerRuntime: resolved.Stack,
            ProfileName: null,
            ProfileBundleVersionRange: null);

        ExtensionBundleResolution resolution = await _bundleResolver.ResolveAsync(context, cancellationToken);
        return resolution is ExtensionBundleResolution.Resolved bundleResolved
            ? new BundleResolution.Resolved(new FuncExtensionBundleContext(bundleResolved.BundleId, bundleResolved.Version))
            : new BundleResolution.Unresolvable(section.Id);
    }

    /// <summary>
    /// Renders a single <see cref="ResolutionFailure"/>. Centralizing the
    /// render here keeps <see cref="ResolveContextAsync"/> side-effect free:
    /// each entry-point renders the failure at most once, regardless of how
    /// many internal resolution passes a caller adds.
    /// </summary>
    private void RenderResolutionFailure(ResolutionFailure failure)
    {
        switch (failure.Kind)
        {
            case ResolutionFailureKind.ProjectRequired:
                _renderer.RenderProjectRequired();
                break;

            case ResolutionFailureKind.MissingLanguage:
                _renderer.RenderMissingLanguage(failure.Stack!, failure.ProjectPath!);
                break;

            default:
                // Guard against silently swallowing a future ResolutionFailureKind.
                throw new UnreachableException(
                    $"Unhandled {nameof(ResolutionFailureKind)}: {failure.Kind}.");
        }
    }

    private async Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        WorkingDirectory workingDirectory,
        ResolvedContext resolved,
        CancellationToken cancellationToken)
        => await _catalog.ListAsync(BuildListContext(workingDirectory, resolved), cancellationToken);

    private static TemplateListContext BuildListContext(WorkingDirectory workingDirectory, ResolvedContext resolved)
        => new(workingDirectory, resolved.Stack, resolved.Language, InstallDirectory: null);

    /// <summary>
    /// Resolves the requested template, prompting via the picker when no
    /// <c>--template</c> was supplied and the shell is interactive. Renders
    /// the appropriate error and returns <c>null</c> when resolution fails.
    /// </summary>
    private async Task<FunctionTemplateInfo?> ResolveTemplateAsync(
        NewInvocation invocation,
        ResolvedContext resolved,
        IReadOnlyList<FunctionTemplateInfo> templates,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(invocation.RequestedTemplate))
        {
            FunctionTemplateInfo? match = ResolveByShortName(templates, invocation.RequestedTemplate);
            if (match is null)
            {
                await RenderUnresolvedTemplateAsync(invocation, resolved, cancellationToken);
            }

            return match;
        }

        bool canPrompt = _interaction.IsInteractive && !invocation.NonInteractive;
        if (!canPrompt)
        {
            _renderer.RenderTemplateRequired();
            return null;
        }

        return await _picker.PickAsync(templates, cancellationToken);
    }

    /// <summary>
    /// Renders the failure for a <c>--template</c> id that resolved to no
    /// selectable template. When the id matches a constraint-restricted
    /// template, surfaces the restriction reason and call-to-action; otherwise
    /// reports it as unknown.
    /// </summary>
    private async Task RenderUnresolvedTemplateAsync(
        NewInvocation invocation,
        ResolvedContext resolved,
        CancellationToken cancellationToken)
    {
        RestrictedTemplateInfo? restricted = await _catalog.FindRestrictedAsync(
            BuildListContext(invocation.WorkingDirectory, resolved), invocation.RequestedTemplate!, cancellationToken);
        if (restricted is not null)
        {
            _renderer.RenderRestrictedTemplate(invocation.RequestedTemplate!, restricted.Reason);
        }
        else
        {
            _renderer.RenderUnknownTemplate(invocation.RequestedTemplate!);
        }
    }

    /// <summary>
    /// Resolves <paramref name="requested"/> against each template's declared
    /// short names (D8/D19), case-insensitively, so legacy aliases like
    /// <c>http</c> / <c>HttpTrigger-TypeScript</c> keep resolving. Falls back
    /// to matching the canonical <see cref="FunctionTemplateInfo.Id"/>.
    /// </summary>
    private static FunctionTemplateInfo? ResolveByShortName(
        IReadOnlyList<FunctionTemplateInfo> templates,
        string requested)
    {
        string trimmed = requested.Trim();
        return templates.FirstOrDefault(t =>
            string.Equals(t.Id, trimmed, StringComparison.OrdinalIgnoreCase)
            || t.ShortNames.Any(s => string.Equals(s, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Builds the stage-B <see cref="ParseResult"/> the scaffolder reads
    /// per-template option values from. Best-effort: the authoritative channel
    /// is <see cref="NewContext.UserOptionValues"/>, so parse errors here are
    /// tolerated (the resulting <see cref="ParseResult"/> simply carries fewer
    /// bound values).
    /// </summary>
    private ParseResult BuildStageBParseResult(
        FunctionTemplateInfo template,
        IReadOnlyDictionary<string, string?>? userValues)
    {
        var root = new RootCommand();
        var byPromptId = new Dictionary<string, Option>(StringComparer.OrdinalIgnoreCase);
        foreach (HydratedTemplateOption pair in _optionHydrator.HydrateWithIds(template))
        {
            root.Options.Add(pair.Option);
            byPromptId[pair.PromptId] = pair.Option;
        }

        List<string> args = [];
        if (userValues is not null)
        {
            foreach ((string promptId, string? value) in userValues)
            {
                if (value is null || !byPromptId.TryGetValue(promptId, out Option? option))
                {
                    continue;
                }

                args.Add(option.Name);
                args.Add(value);
            }
        }

        return root.Parse([.. args]);
    }

    private int RenderApplyResult(
        FunctionTemplateInfo template,
        string functionName,
        TemplateApplicationResult result,
        bool jsonOutput)
    {
        switch (result)
        {
            case TemplateApplicationResult.Created created:
                if (jsonOutput)
                {
                    _renderer.RenderCreatedJson(template, functionName, created.Files, created.Modified, created.Messages);
                }
                else
                {
                    _renderer.RenderCreated(template, functionName, created.Files, created.Modified, created.Messages);
                }

                return 0;

            case TemplateApplicationResult.AlreadyExists alreadyExists:
                _renderer.RenderAlreadyExists(alreadyExists.ExistingFiles);
                return 1;

            case TemplateApplicationResult.Failed failed:
                _renderer.RenderApplyFailure(failed.Failure);
                return 1;

            default:
                throw new UnreachableException(
                    $"Unhandled {nameof(TemplateApplicationResult)}: {result.GetType().Name}.");
        }
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

    private sealed record ResolvedContext(string Stack, string Language);

    private enum ResolutionFailureKind
    {
        ProjectRequired,
        MissingLanguage,
    }

    private sealed record ResolutionFailure(
        ResolutionFailureKind Kind,
        string? Stack = null,
        string? ProjectPath = null);

    private readonly struct ResolutionOutcome
    {
        private ResolutionOutcome(ResolvedContext? context, ResolutionFailure? failure)
        {
            Context = context;
            Failure = failure;
        }

        public ResolvedContext? Context { get; }

        public ResolutionFailure? Failure { get; }

        public static ResolutionOutcome Succeed(ResolvedContext context) => new(context, null);

        public static ResolutionOutcome Fail(ResolutionFailure failure) => new(null, failure);
    }

    private abstract record BundleResolution
    {
        private BundleResolution()
        {
        }

        public sealed record NotApplicable : BundleResolution
        {
            public static NotApplicable Instance { get; } = new();
        }

        public sealed record Resolved(FuncExtensionBundleContext Context) : BundleResolution;

        public sealed record Unresolvable(string SuggestedBundleId) : BundleResolution;
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
    bool NonInteractive,
    bool JsonOutput = false,
    IReadOnlyDictionary<string, string?>? UserOptionValues = null);
