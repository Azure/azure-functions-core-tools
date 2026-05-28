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

        ResolvedContext? resolved = await ResolveContextAsync(invocation, cancellationToken);
        if (resolved is null)
        {
            return 1;
        }

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
        IReadOnlyDictionary<string, string?> optionValues = BuildPromptDefaults(template);

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
            resolved.Workload.InstallDirectory);

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

        ResolvedContext? resolved = await ResolveContextAsync(invocation, cancellationToken);
        if (resolved is null)
        {
            return 1;
        }

        IReadOnlyList<FunctionTemplateInfo> templates = await ListTemplatesAsync(resolved, cancellationToken);
        if (templates.Count == 0)
        {
            _renderer.RenderNoTemplatesWorkloadInstalled(resolved.Stack);
            return 1;
        }

        _renderer.RenderCatalogue(resolved.Stack, resolved.Language, templates);
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
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

        ResolvedContext? resolved = await ResolveContextAsync(invocation, cancellationToken);
        if (resolved is null)
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

        return _optionHydrator.Hydrate(template);
    }

    private async Task<ResolvedContext?> ResolveContextAsync(
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
            _renderer.RenderProjectRequired();
            return null;
        }

        string stack = resolved.Project.StackName;

        // Step 4 (Node/Python): channel match against host.json.
        InstalledTemplatesWorkload? workload;
        string? bundleId = null;
        string? channelLabel = null;

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
                _interaction.WriteError(
                    "Cannot resolve templates: host.json declares no extension bundle.");
                _interaction.WriteLine(l => l
                    .Muted("Configure ")
                    .Code("extensionBundle.id")
                    .Muted(" in host.json or run ")
                    .Code("func init")
                    .Muted(" with a stack that declares one."));
                return null;
            }

            bundleId = section.Id;
            channelLabel = TemplatesChannelMapper.GetChannelLabel(bundleId);
            if (channelLabel is null)
            {
                _interaction.WriteError($"Unrecognised extension bundle id '{bundleId}'.");
                _interaction.WriteLine(l => l
                    .Muted("Use one of: ")
                    .Code(TemplatesChannelMapper.StableBundleId)
                    .Muted(", ")
                    .Code(TemplatesChannelMapper.PreviewBundleId)
                    .Muted(", or ")
                    .Code(TemplatesChannelMapper.ExperimentalBundleId)
                    .Muted("."));
                return null;
            }

            IReadOnlyList<InstalledTemplatesWorkload> allRows =
                await _installedTemplatesWorkloads.ListInstalledAsync(stack, cancellationToken);
            workload = TemplatesChannelMapper.PickChannelMatched(allRows, channelLabel);
        }

        if (workload is null)
        {
            if (channelLabel is null)
            {
                _renderer.RenderNoTemplatesWorkloadInstalled(stack);
            }
            else
            {
                string channelName = TemplatesChannelMapper.GetChannelDisplayName(channelLabel);
                string suggestedPkg = TemplatesWorkloadConstants.GetPackageId(stack);
                string suggestedVer = string.IsNullOrEmpty(channelLabel)
                    ? "<version>"
                    : $"<version>-{channelLabel}";
                _interaction.WriteError(
                    $"No installed templates workload matches this project's bundle channel " +
                    $"({bundleId} -> channel '{channelName}').");
                _interaction.WriteLine(l => l
                    .Muted("Install one with: ")
                    .Code($"func workload install {suggestedPkg} --version {suggestedVer}")
                    .Muted("."));
            }
            return null;
        }

        // Step 5: language resolution via IOptionsMonitor<StackOptions>.
        string projectDirectory = Path.GetFullPath(invocation.WorkingDirectory.Info.FullName);
        StackOptions stackOptionsBound = _stackOptions.Get(projectDirectory);
        string? language = ResolveLanguage(stack, stackOptionsBound);
        if (language is null)
        {
            _renderer.RenderMissingLanguage(stack, projectDirectory);
            return null;
        }

        return new ResolvedContext(
            invocation.WorkingDirectory,
            stack,
            language,
            workload,
            bundleId,
            channelLabel);
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

    private static IReadOnlyDictionary<string, string?> BuildPromptDefaults(FunctionTemplateInfo template)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateUserPrompt prompt in template.Metadata.UserPrompts)
        {
            dict[prompt.Id] = prompt.DefaultValue;
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
        string? ChannelLabel);
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
