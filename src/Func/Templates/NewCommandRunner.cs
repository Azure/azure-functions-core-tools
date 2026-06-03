// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
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
    private readonly IHostJsonBundleSectionReader _hostJsonReader;
    private readonly IExtensionBundleResolver _bundleResolver;

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
        NewCommandRenderer renderer,
        IHostJsonBundleSectionReader hostJsonReader,
        IExtensionBundleResolver bundleResolver)
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
        _hostJsonReader = hostJsonReader ?? throw new ArgumentNullException(nameof(hostJsonReader));
        _bundleResolver = bundleResolver ?? throw new ArgumentNullException(nameof(bundleResolver));

        _projectInitializersByStack = projectInitializers
            .Where(p => !string.IsNullOrWhiteSpace(p.Stack))
            .GroupBy(p => p.Stack.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<int> ExecuteAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        ResolutionOutcome outcome = await ResolveContextAsync(invocation, cancellationToken);
        if (outcome.Failure is { } executeFailure)
        {
            RenderResolutionFailure(executeFailure);
            return 1;
        }

        ResolvedContext resolved = outcome.Context!;

        // Steps 11a / 11b: extension-bundle presence + min-bundle gate.
        // DotNet doesn't ship a templates-workload.json, so the gate is a
        // no-op for it; Node and Python carry one.
        int bundleGate = await EnforceBundleGatesAsync(resolved, cancellationToken);
        if (bundleGate != 0)
        {
            return bundleGate;
        }

        // Step 6: aggregate templates from every registered engine for the
        // active stack.
        IReadOnlyList<FunctionTemplateInfo> templates = await ListTemplatesAsync(resolved, cancellationToken);
        if (templates.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(resolved.Stack);
            return 1;
        }

        // Step 7: resolve --template, falling back to the picker in
        // interactive mode (errors when neither --template nor an
        // interactive shell is available).
        FunctionTemplateInfo? template = await ResolveTemplateAsync(
            invocation, templates, cancellationToken);
        if (template is null)
        {
            return 1;
        }

        // Step 8 / 9: stage-B hydration. Hands the engine the user's --name
        // plus each prompt's declared default (no per-option CLI parsing
        // yet); the engine reads from the supplied dictionary.
        IReadOnlyDictionary<string, string?> optionValues = BuildPromptDefaults(template, invocation.UserOptionValues);

        // Step 10: function name resolution.
        string functionName = invocation.RequestedFunctionName
            ?? template.DefaultFunctionName
            ?? template.Id;

        // Step 12: dispatch to the engine.
        ITemplateEngineProvider? provider = _engineProviders.TryGet(template.EngineId);
        if (provider is null)
        {
            _interaction.WriteError(
                $"No engine registered for EngineId '{template.EngineId}'. This is a CLI bug.");
            return 1;
        }

        // Hydrate options against the chosen template so the engine's
        // ParseResult consumers see the right option set. Hydration runs but
        // defers user-supplied per-prompt overrides — the engine resolves
        // values primarily from `optionValues`.
        IReadOnlyList<Option> hydrated = _optionHydrator.Hydrate(template);
        _ = hydrated;

        var context = new NewContext(
            invocation.WorkingDirectory,
            template,
            functionName,
            resolved.Language,
            invocation.Force,
            resolved.Workload.InstallDirectory,
            invocation.UserOptionValues);

        ParseResult emptyParseResult = new RootCommand().Parse(string.Empty);
        TemplateApplicationResult applyResult = await provider.ApplyAsync(context, emptyParseResult, cancellationToken);

        return RenderApplyResult(template, applyResult, optionValues);
    }

    /// <summary>
    /// Lists templates for <c>func new --list</c>. Same resolution
    /// gates as <see cref="ExecuteAsync"/> minus the stage-B / apply tail.
    /// </summary>
    public async Task<int> ListAsync(NewInvocation invocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        ResolutionOutcome outcome = await ResolveContextAsync(invocation, cancellationToken);
        if (outcome.Failure is { } listFailure)
        {
            RenderResolutionFailure(listFailure);
            return 1;
        }

        ResolvedContext resolved = outcome.Context!;

        IReadOnlyList<FunctionTemplateInfo> templates = await ListTemplatesAsync(resolved, cancellationToken);
        if (templates.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(resolved.Stack);
            return 1;
        }

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
    /// Resolves the single template identified by <paramref name="templateId"/>
    /// for the project at <paramref name="invocation"/>, then hands back the
    /// hydrated <see cref="Option"/> list the stage-B help renderer needs.
    /// Returns <c>null</c> when the project can't be resolved, the templates
    /// workload isn't installed, or <paramref name="templateId"/> doesn't
    /// match any catalogued template — the caller decides whether to fall
    /// back to a built-ins-only help render or surface the failure.
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
    /// overload on the execute path to map user-supplied values back to
    /// the v2 paramId the engine resolves against.
    /// </summary>
    /// <remarks>
    /// Pre-parse / hydration callers are best-effort: when resolution fails
    /// they return <c>null</c> silently. Rendering the failure is the job of
    /// the execute / list entry-points, which call <see cref="ResolveContextAsync"/>
    /// themselves; rendering here too would surface the same error twice
    /// (or three times, once #5304's third call site lands) for one invocation.
    /// </remarks>
    public async Task<IReadOnlyList<HydratedTemplateOption>?> HydrateOptionsForTemplateWithIdsAsync(
        NewInvocation invocation,
        string templateId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        ResolutionOutcome outcome = await ResolveContextAsync(invocation, cancellationToken);
        if (outcome.Context is not { } resolved)
        {
            return null;
        }

        IReadOnlyList<FunctionTemplateInfo> templates = await ListTemplatesAsync(resolved, cancellationToken);
        FunctionTemplateInfo? template = templates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));

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
        // Step 1: resolve the active profile. Diagnostics are surfaced by
        // the resolver itself; the stack-vs-profile gate is wired in a
        // follow-up commit.
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

        // Step 4 (Node/Python): channel match against host.json.
        InstalledTemplatesWorkload? workload;
        string? bundleId = null;
        BundleChannel channel = BundleChannel.Unknown;
        if (string.Equals(stack, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<InstalledTemplatesWorkload> allRows =
                await _installedTemplatesWorkloads.ListInstalledAsync(stack, cancellationToken);
            workload = allRows
                .OrderByDescending(r => r.PackageVersion, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        else
        {
            HostJsonBundleSection? section = await _hostJsonReader.ReadAsync(
                invocation.WorkingDirectory.Info, cancellationToken);
            if (section is null || string.IsNullOrWhiteSpace(section.Id))
            {
                return ResolutionOutcome.Fail(new ResolutionFailure(ResolutionFailureKind.HostJsonBundleMissing));
            }

            bundleId = section.Id;
            if (!BundleHelpers.TryGetBundleChannel(bundleId, out channel))
            {
                return ResolutionOutcome.Fail(new ResolutionFailure(
                    ResolutionFailureKind.UnrecognisedBundleId,
                    BundleId: bundleId));
            }

            IReadOnlyList<InstalledTemplatesWorkload> allRows =
                await _installedTemplatesWorkloads.ListInstalledAsync(stack, cancellationToken);
            workload = TemplatesChannelMapper.PickChannelMatched(allRows, channel);
        }

        if (workload is null)
        {
            if (channel is BundleChannel.Unknown)
            {
                return ResolutionOutcome.Fail(new ResolutionFailure(
                    ResolutionFailureKind.NoTemplatesWorkloadInstalled,
                    Stack: stack));
            }

            return ResolutionOutcome.Fail(new ResolutionFailure(
                ResolutionFailureKind.NoTemplatesWorkloadForChannel,
                Stack: stack,
                BundleId: bundleId,
                Channel: channel));
        }

        // Step 5: language resolution via IOptionsMonitor<StackOptions>.
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

        return ResolutionOutcome.Succeed(new ResolvedContext(
            invocation.WorkingDirectory,
            stack,
            language,
            workload,
            bundleId,
            channel));
    }

    /// <summary>
    /// Renders a single <see cref="ResolutionFailure"/>. Centralizing the
    /// render here keeps <see cref="ResolveContextAsync"/> side-effect free:
    /// each <see cref="ExecuteAsync"/> / <see cref="ListAsync"/> entry-point
    /// renders the failure at most once, regardless of how many internal
    /// resolution passes a caller adds (pre-parse / hydration paths run the
    /// same resolver but consume the outcome silently).
    /// </summary>
    private void RenderResolutionFailure(ResolutionFailure failure)
    {
        switch (failure.Kind)
        {
            case ResolutionFailureKind.ProjectRequired:
                _renderer.RenderProjectRequired();
                break;

            case ResolutionFailureKind.HostJsonBundleMissing:
                _interaction.WriteError(
                    "Cannot resolve templates: host.json declares no extension bundle.");
                _interaction.WriteLine(l => l
                    .Muted("Configure ")
                    .Code("extensionBundle.id")
                    .Muted(" in host.json or run ")
                    .Code("func init")
                    .Muted(" with a stack that declares one."));
                break;

            case ResolutionFailureKind.UnrecognisedBundleId:
                _interaction.WriteError($"Unrecognized extension bundle id '{failure.BundleId}'.");
                _interaction.WriteLine(l => l
                    .Muted("Use one of: ")
                    .Code(BundleHelpers.StableBundleId)
                    .Muted(", ")
                    .Code(BundleHelpers.PreviewBundleId)
                    .Muted(", or ")
                    .Code(BundleHelpers.ExperimentalBundleId)
                    .Muted("."));
                break;

            case ResolutionFailureKind.NoTemplatesWorkloadInstalled:
                _renderer.RenderNoTemplatesWorkloadInstalled(failure.Stack!);
                break;

            case ResolutionFailureKind.NoTemplatesWorkloadForChannel:
                {
                    string channelName = failure.Channel.ToDisplayString();
                    string suggestedPkg = TemplatesWorkloadConstants.GetPackageId(failure.Stack!);
                    string suggestedVer = failure.Channel == BundleChannel.Stable
                        ? "<version>"
                        : $"<version>-{channelName}.1";
                    _interaction.WriteError(
                        $"No installed templates workload matches this project's bundle channel " +
                        $"({failure.BundleId} -> channel '{channelName}').");
                    _interaction.WriteLine(l => l
                        .Muted("Install one with: ")
                        .Code($"func workload install {suggestedPkg} --version {suggestedVer}")
                        .Muted("."));
                    break;
                }

            case ResolutionFailureKind.MissingLanguage:
                _renderer.RenderMissingLanguage(failure.Stack!, failure.ProjectPath!);
                break;
        }
    }

    private async Task<int> EnforceBundleGatesAsync(
        ResolvedContext resolved,
        CancellationToken cancellationToken)
    {
        // DotNet skips both gates (no extension-bundle dependency).
        if (string.Equals(resolved.Stack, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (resolved.BundleId is null)
        {
            return 0;
        }

        // Step 11a: bundle presence via IExtensionBundleResolver.
        HostJsonBundleSection? section = await _hostJsonReader.ReadAsync(
            resolved.WorkingDirectory.Info, cancellationToken);
        if (section is null)
        {
            return 0;
        }

        var context = new ExtensionBundleProjectContext(
            BundleId: section.Id,
            HostJsonVersionRange: section.Version,
            WorkerRuntime: resolved.Stack,
            ProfileName: null,
            ProfileBundleVersionRange: null);

        ExtensionBundleResolution resolution = await _bundleResolver.ResolveAsync(context, cancellationToken);
        switch (resolution)
        {
            case ExtensionBundleResolution.Resolved bundleResolved:
                // Step 11b: min-bundle range from the templates
                // workload's sibling manifest.
                string? minRange = TemplatesWorkloadManifestReader.GetMinBundleVersion(resolved.Workload.InstallDirectory);
                if (!string.IsNullOrWhiteSpace(minRange)
                    && !VersionRangeContains(minRange, bundleResolved.Version))
                {
                    _interaction.WriteError(
                        $"Installed templates workload '{resolved.Workload.PackageVersion}' requires " +
                        $"extension bundle in range '{minRange}', but the project resolves to '{bundleResolved.Version}'.");
                    _interaction.WriteLine(l => l
                        .Muted("Update the bundle range in ")
                        .Code("host.json")
                        .Muted(" or install an older templates workload pkg version."));
                    return 1;
                }
                return 0;

            case ExtensionBundleResolution.WorkloadMissing:
            case ExtensionBundleResolution.EmptyIntersection:
                _interaction.WriteError("The project requires an extension bundle but none is resolvable.");
                _interaction.WriteLine(l => l
                    .Muted("Install one with: ")
                    .Code("func workload install Azure.Functions.Cli.Workloads.ExtensionBundles")
                    .Muted(" or run ")
                    .Code("func setup")
                    .Muted("."));
                return 1;

            default:
                return 0;
        }
    }

    private async Task<IReadOnlyList<FunctionTemplateInfo>> ListTemplatesAsync(
        ResolvedContext resolved,
        CancellationToken cancellationToken)
    {
        var listContext = new TemplateListContext(
            resolved.WorkingDirectory,
            resolved.Stack,
            resolved.Language,
            resolved.Workload.InstallDirectory);
        List<FunctionTemplateInfo> templates = [];
        foreach (ITemplateEngineProvider provider in _engineProviders.Providers)
        {
            IReadOnlyList<FunctionTemplateInfo> contributed = await provider.ListTemplatesAsync(listContext, cancellationToken);
            templates.AddRange(contributed);
        }

        return templates;
    }

    private async Task<FunctionTemplateInfo?> ResolveTemplateAsync(
        NewInvocation invocation,
        IReadOnlyList<FunctionTemplateInfo> templates,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(invocation.RequestedTemplate))
        {
            FunctionTemplateInfo? matched = templates.FirstOrDefault(t =>
                string.Equals(t.Id, invocation.RequestedTemplate, StringComparison.OrdinalIgnoreCase));
            if (matched is null)
            {
                _interaction.WriteError(
                    $"Template '{invocation.RequestedTemplate}' was not found for this project's stack.");
                _interaction.WriteLine(l => l
                    .Muted("Run ")
                    .Code("func new --list")
                    .Muted(" to see available templates."));
                return null;
            }

            return matched;
        }

        if (invocation.NonInteractive || !_interaction.IsInteractive)
        {
            _interaction.WriteError("Missing required option: --template.");
            _interaction.WriteLine(l => l
                .Muted("Pass ")
                .Code("--template <id>")
                .Muted(" or run interactively to pick one. ")
                .Code("func new --list")
                .Muted(" shows available templates."));
            return null;
        }

        return await _picker.PickAsync(templates, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string?> BuildPromptDefaults(
        FunctionTemplateInfo template,
        IReadOnlyDictionary<string, string?>? userOverrides = null)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateUserPrompt prompt in template.Metadata.UserPrompts)
        {
            dict[prompt.Id] = prompt.DefaultValue;
        }

        if (userOverrides is not null)
        {
            foreach (KeyValuePair<string, string?> pair in userOverrides)
            {
                dict[pair.Key] = pair.Value;
            }
        }

        return dict;
    }

    private int RenderApplyResult(
        FunctionTemplateInfo template,
        TemplateApplicationResult result,
        IReadOnlyDictionary<string, string?> _)
    {
        switch (result)
        {
            case TemplateApplicationResult.Created created:
                _interaction.WriteSuccess($"Created function '{template.Id}'.");
                foreach (string file in created.Files)
                {
                    _interaction.WriteLine(l => l.Muted("  ").Code(file));
                }
                return 0;

            case TemplateApplicationResult.AlreadyExists existing:
                _interaction.WriteError("Some files already exist:");
                foreach (string file in existing.ExistingFiles)
                {
                    _interaction.WriteLine(l => l.Muted("  ").Code(file));
                }
                _interaction.WriteLine(l => l
                    .Muted("Re-run with ")
                    .Code("--force")
                    .Muted(" to overwrite."));
                return 1;

            case TemplateApplicationResult.Failed failed:
                RenderFailure(failed.Failure);
                return 1;

            default:
                _interaction.WriteError("Unknown apply result.");
                return 1;
        }
    }

    private void RenderFailure(TemplateApplicationFailure failure)
    {
        switch (failure)
        {
            case TemplateApplicationFailure.WriteFailed write:
                _interaction.WriteError($"Failed to write '{write.Path}': {write.Message}");
                break;
            case TemplateApplicationFailure.InvalidPrompt prompt:
                _interaction.WriteError($"Invalid prompt '{prompt.PromptId}': {prompt.Reason}");
                break;
            case TemplateApplicationFailure.ProviderError provider:
                _interaction.WriteError(provider.Message);
                break;
            case TemplateApplicationFailure.MissingExtensionBundle bundle:
                _interaction.WriteError($"Stack '{bundle.Stack}' requires extension bundle '{bundle.SuggestedBundleId}', which is not installed.");
                break;
            case TemplateApplicationFailure.MinBundleVersionTooOld min:
                _interaction.WriteError(
                    $"Installed bundle '{min.InstalledBundleVersion}' is outside required range '{min.RequiredRange}' for templates workload '{min.TemplatesWorkloadVersion}'.");
                break;
            case TemplateApplicationFailure.NoTemplatesWorkloadForChannel ntw:
                _interaction.WriteError(
                    $"No templates workload installed for channel '{ntw.Channel}' on stack '{ntw.Stack}'.");
                _interaction.WriteLine(l => l
                    .Muted("Install one with: ")
                    .Code($"func workload install {ntw.SuggestedPackageId} --version {ntw.SuggestedVersion}")
                    .Muted("."));
                break;
            default:
                _interaction.WriteError("Template application failed.");
                break;
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

    /// <summary>
    /// Minimal NuGet-style range containment check for v1 — accepts the
    /// open-ended forms <c>[X.Y.Z, )</c> and bare <c>X.Y.Z</c> (treated as
    /// "min X.Y.Z, no upper bound"). A follow-up can swap in NuGetVersion.
    /// </summary>
    internal static bool VersionRangeContains(string range, string version)
    {
        if (string.IsNullOrWhiteSpace(range) || string.IsNullOrWhiteSpace(version))
        {
            return true;
        }

        string trimmed = range.Trim();
        string lowerBound = trimmed;
        if (trimmed.StartsWith('[') || trimmed.StartsWith('('))
        {
            int comma = trimmed.IndexOf(',');
            if (comma > 1)
            {
                lowerBound = trimmed[1..comma].Trim();
            }
            else
            {
                lowerBound = trimmed.Trim('[', '(', ']', ')').Trim();
            }
        }

        return CompareVersions(version, lowerBound) >= 0;
    }

    private static int CompareVersions(string a, string b)
    {
        if (Version.TryParse(StripPrerelease(a), out Version? va)
            && Version.TryParse(StripPrerelease(b), out Version? vb))
        {
            return va.CompareTo(vb);
        }

        return string.Compare(a, b, StringComparison.Ordinal);
    }

    private static string StripPrerelease(string version)
    {
        int dash = version.IndexOf('-');
        return dash < 0 ? version : version[..dash];
    }

    private sealed record ResolvedContext(
        WorkingDirectory WorkingDirectory,
        string Stack,
        string Language,
        InstalledTemplatesWorkload Workload,
        string? BundleId,
        BundleChannel Channel);

    private enum ResolutionFailureKind
    {
        ProjectRequired,
        HostJsonBundleMissing,
        UnrecognisedBundleId,
        NoTemplatesWorkloadInstalled,
        NoTemplatesWorkloadForChannel,
        MissingLanguage,
    }

    private sealed record ResolutionFailure(
        ResolutionFailureKind Kind,
        string? Stack = null,
        string? ProjectPath = null,
        string? BundleId = null,
        BundleChannel Channel = BundleChannel.Unknown);

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
