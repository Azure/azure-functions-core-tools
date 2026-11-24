// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Hosting;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Workloads;

namespace Azure.Functions.Cli.Commands;

/// <summary>
/// Scaffolds new projects using an installed initializer, or adopts existing projects without modifying their source.
/// </summary>
internal class InitCommand : FuncCliCommand, IBuiltInCommand
{
    public Option<string?> StackOption { get; } = new("--stack", "-s");

    public Option<string?> NameOption { get; } = new("--name", "-n") { Description = "The name of the Function App project" };

    public Option<string?> LanguageOption { get; } = new("--language", "-l");

    public Option<bool> ForceOption { get; } = new("--force")
    {
        Description = "Re-initialize the directory: deletes its contents (except .git) before scaffolding the new project."
    };

    private readonly IInteractionService _interaction;
    private readonly IWorkloadHintRenderer _hintRenderer;
    private readonly IProjectStackConfiguration _configuration;
    private readonly IFunctionsProjectResolver _projectResolver;
    private readonly IHostJsonBundleSectionReader _bundleSectionReader;
    private readonly InstalledBundleScanner _bundleScanner;
    private readonly InstalledProjectStackCatalog _catalog;
    private readonly IReadOnlyList<IProjectInitializer> _initializers;

    public InitCommand(IInteractionService interaction, IWorkloadHintRenderer hintRenderer, IProjectStackConfiguration configuration,
        IFunctionsProjectResolver projectResolver, IHostJsonBundleSectionReader bundleSectionReader, InstalledBundleScanner bundleScanner,
        InstalledProjectStackCatalog catalog, IEnumerable<IProjectInitializer> initializers)
        : base("init", "Initialize a new Azure Functions project.")
    {
        _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        _hintRenderer = hintRenderer ?? throw new ArgumentNullException(nameof(hintRenderer));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _projectResolver = projectResolver ?? throw new ArgumentNullException(nameof(projectResolver));
        _bundleSectionReader = bundleSectionReader ?? throw new ArgumentNullException(nameof(bundleSectionReader));
        _bundleScanner = bundleScanner ?? throw new ArgumentNullException(nameof(bundleScanner));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _initializers = (initializers ?? throw new ArgumentNullException(nameof(initializers))).ToList();

        StackOption.Description = BuildOptionDescription("The stack to use", _initializers.Select(i => i.ProjectStack.Descriptor.StackIdentifier));
        LanguageOption.Description = BuildOptionDescription("The programming language", _initializers.SelectMany(i => i.ProjectStack.SupportedLanguages).Select(l => l.LanguageIdentifier));
        AddPathArgument();
        Options.Add(StackOption);
        Options.Add(NameOption);
        Options.Add(LanguageOption);
        Options.Add(ForceOption);

        var registry = new InitOptionRegistry(this);
        foreach (IProjectInitializer initializer in _initializers)
        {
            StackDescriptor descriptor = initializer.ProjectStack.Descriptor;
            registry.SetActiveStack(descriptor.StackIdentifier);
            IReadOnlyList<Option> options = initializer.GetInitOptions(registry);
            RegisterStackOptionHelp(descriptor, options.Where(option => option.Name != CommonInitOptions.NoBundleOptionName
                && option.Name != CommonInitOptions.BundlesChannelOptionName));
        }
    }

    protected override string HelpFooterHint => "Looking for more stacks? Run `func workload search --stack` to list installable stack workloads.";

    protected override async Task<int> ExecuteAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        try
        {
            return await InitializeAsync(parseResult, cancellationToken);
        }
        catch (Exception ex) when (ex is ProjectStackResolutionException or ProjectStackConfigurationException)
        {
            _interaction.WriteError(ex.Message);
            return 1;
        }
    }

    private async Task<int> InitializeAsync(ParseResult parseResult, CancellationToken cancellationToken)
    {
        WorkingDirectory directory = parseResult.GetValue(PathArgument!)!;
        bool force = parseResult.GetValue(ForceOption);
        ProjectStackSettings settings = force ? new() : _configuration.Read(directory.Info);
        string? explicitLanguage = parseResult.GetValue(LanguageOption);
        IProjectInitializer? initializer = await SelectInitializerAsync(parseResult.GetValue(StackOption), settings, cancellationToken);
        if (initializer is null)
        {
            return 1;
        }

        IProjectStack stack = initializer.ProjectStack;
        LanguageDescriptor? language = explicitLanguage is null ? null : InstalledProjectStackCatalog.ResolveLanguage(stack, explicitLanguage, "--language");
        if (settings.Language is { } persistedLanguage)
        {
            LanguageDescriptor persisted = InstalledProjectStackCatalog.ResolveLanguage(stack, persistedLanguage, ProjectStackSettings.LanguageSource);
            if (language is not null && persisted != language)
            {
                throw new ProjectStackResolutionException($"--language '{explicitLanguage}' conflicts with {ProjectStackSettings.LanguageSource} '{persistedLanguage}'.");
            }
        }

        bool complete = settings.HasProjectConfig && settings.Language is null && stack.SupportedLanguages.Count > 1;
        if (settings.HasProjectConfig && !complete)
        {
            _interaction.WriteError($"This directory already contains a Functions project ('{CliConfigurationPathsOptions.ProjectConfigDisplayPath}' is present). Pass --force to re-initialize.");
            return 1;
        }

        if (settings.IsAdoptable || complete)
        {
            LanguageDescriptor? detected = await DetectProjectLanguageAsync(directory, stack, cancellationToken);
            if (language is not null && detected is not null && detected != language)
            {
                throw new ProjectStackResolutionException($"--language '{explicitLanguage}' conflicts with detected project language '{detected.LanguageIdentifier}' for stack '{stack.Descriptor.StackIdentifier}'.");
            }

            language ??= detected;
        }

        // A pre-v5 project with no language fingerprints may be adopted with just its resolved stack.
        // Repairing an existing config, unlike adoption, must actually fill the missing language.
        if (language is null && !settings.IsAdoptable)
        {
            language = await SelectLanguageAsync(stack, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (force && directory.Exists && DirectoryGuard.HasNonGitContent(directory.Info))
        {
            _interaction.WriteWarning($"--force will delete all files in '{directory.Info.FullName}' (except .git) before initializing.");
            if (!await _interaction.ConfirmAsync("Continue?", defaultValue: false, whenInputUnavailable: true, cancellationToken))
            {
                _interaction.WriteHint("Init cancelled. The directory was not modified.");
                return 1;
            }

            cancellationToken.ThrowIfCancellationRequested();
            DirectoryGuard.ClearExceptGit(directory.Info);
        }

        if (complete)
        {
            await _configuration.CompleteLanguageAsync(directory.Info, stack, language!, cancellationToken);
            WriteSuccess("Existing project completed for ", stack);
            return 0;
        }

        await _configuration.CreateAsync(directory.Info, stack, language, cancellationToken);
        _interaction.WriteLine(l => l.Muted("· Wrote ").Code(CliConfigurationPathsOptions.ProjectConfigDisplayPath).Muted("."));
        if (settings.IsAdoptable)
        {
            WriteSuccess("Existing project adopted for ", stack);
            return 0;
        }

        directory.Info.Refresh();
        var context = new InitContext(directory, parseResult.GetValue(NameOption), language, force);
        await initializer.InitializeAsync(context, parseResult, cancellationToken);
        WriteSuccess("Project initialized for ", stack);
        await WarnIfBundleChannelNotInstalledAsync(directory.Info, cancellationToken);
        return 0;
    }

    private async Task<IProjectInitializer?> SelectInitializerAsync(string? requested, ProjectStackSettings settings, CancellationToken cancellationToken)
    {
        // Check the installed inventory independently of this command's capabilities. An initializer must not hide a second package claiming its ID.
        IReadOnlyList<IProjectStack> installed = _catalog.Stacks;
        Dictionary<string, IProjectInitializer> eligible = [with(StringComparer.OrdinalIgnoreCase)];
        foreach (IProjectInitializer initializer in _initializers)
        {
            string id = initializer.ProjectStack.Descriptor.StackIdentifier;
            if (!eligible.TryAdd(id, initializer))
            {
                throw new ProjectStackResolutionException($"Multiple installed initializers claim the '{id}' stack.");
            }
            if (!installed.Any(owner => ReferenceEquals(owner, initializer.ProjectStack)))
            {
                throw new ProjectStackResolutionException($"The initializer for '{id}' does not reference its registered project stack.");
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
            if (eligible.TryGetValue(selected.Descriptor.StackIdentifier, out IProjectInitializer? initializer))
            {
                return initializer;
            }

            throw new ProjectStackResolutionException($"Stack '{selected.Descriptor.StackIdentifier}' does not provide an initializer for 'func init'.");
        }

        if (eligible.Count == 0)
        {
            RenderHint(WorkloadHintKind.NoWorkloadsInstalled);
            return null;
        }

        if (!settings.IsAdoptable && _initializers is [var sole])
        {
            RenderHint(WorkloadHintKind.AutoSelectedSoleWorkload, sole.ProjectStack.Descriptor.StackIdentifier);
            return sole;
        }

        if (!_interaction.IsInteractive)
        {
            if (settings.IsAdoptable)
            {
                _interaction.WriteError("Couldn't detect the project's stack from local.settings.json. Re-run with --stack <id> to select the stack to adopt.");
            }
            else
            {
                RenderHint(WorkloadHintKind.AmbiguousStackChoice);
            }
            return null;
        }

        List<StackDescriptor> choices = [.. _initializers.Select(i => i.ProjectStack.Descriptor)];
        StackDescriptor picked = await _interaction.PromptForSelectionAsync(
            settings.IsAdoptable ? "Adopting an existing project. Which stack is it?" : "Select a stack:", choices,
            d => choices.Count(c => c.DisplayName.Equals(d.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1 ? $"{d.DisplayName} ({d.StackIdentifier})" : d.DisplayName,
            cancellationToken);
        return eligible[picked.StackIdentifier];
    }

    private async Task<LanguageDescriptor?> DetectProjectLanguageAsync(WorkingDirectory directory, IProjectStack stack, CancellationToken cancellationToken)
    {
        ProjectResolutionResult result = await _projectResolver.ResolveProjectAsync(new ProjectResolutionContext(directory, stack), cancellationToken);
        if (result is ProjectResolutionResult.NotResolved notResolved)
        {
            return notResolved.IsUnrecognized ? null : throw new ProjectStackResolutionException(notResolved.Message);
        }

        FunctionsProject project = ((ProjectResolutionResult.Resolved)result).Project;
        if (project.ProjectStack.Descriptor != stack.Descriptor)
        {
            throw new ProjectStackResolutionException($"Selected stack '{stack.Descriptor.StackIdentifier}' conflicts with detected project stack '{project.ProjectStack.Descriptor.StackIdentifier}'.");
        }

        return project.Language is not { } language ? null
            : stack.SupportedLanguages.SingleOrDefault(l => l == language) ?? throw new ProjectStackResolutionException(
                $"Detected project language '{language.LanguageIdentifier}' is not supported by stack '{stack.Descriptor.StackIdentifier}'.");
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
            throw new ProjectStackResolutionException($"The '{stack.Descriptor.StackIdentifier}' stack supports multiple languages. Re-run with --language <{string.Join("|", choices.Select(l => l.LanguageIdentifier))}>.");
        }

        return await _interaction.PromptForSelectionAsync("Select a language:", choices,
            l => choices.Count(c => c.DisplayName.Equals(l.DisplayName, StringComparison.OrdinalIgnoreCase)) > 1 ? $"{l.DisplayName} ({l.LanguageIdentifier})" : l.DisplayName,
            cancellationToken);
    }

    private void RenderHint(WorkloadHintKind kind, string? requested = null)
        => _hintRenderer.Render(new WorkloadHint(kind, "initialize a project", requested, [.. _initializers.Select(i => i.ProjectStack.Descriptor.StackIdentifier)]));

    private void WriteSuccess(string action, IProjectStack stack)
    {
        _interaction.WriteBlankLine();
        _interaction.WriteLine(l => l.Success("✓ ").Muted(action).Code(stack.Descriptor.DisplayName).Muted("."));
    }

    private static string BuildOptionDescription(string description, IEnumerable<string> values)
    {
        string supported = string.Join(", ", values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        return supported.Length == 0 ? $"{description}. Set up a stack (`func setup --features <id>`) to see supported values."
            : $"{description}. Supported values: {supported}.";
    }

    private async Task WarnIfBundleChannelNotInstalledAsync(DirectoryInfo projectDirectory, CancellationToken cancellationToken)
    {
        HostJsonBundleSection? bundle = await _bundleSectionReader.ReadAsync(projectDirectory, cancellationToken);
        if (bundle is null || !BundleHelpers.TryGetBundleChannel(bundle.Id, out BundleChannel channel) || channel == BundleChannel.Stable)
        {
            return;
        }

        ScanResult scan = await _bundleScanner.ScanAsync(channel, cancellationToken);
        if (scan.FilteredVersions.Count == 0)
        {
            _interaction.WriteWarning($"This project targets the '{channel.ToDisplayString()}' extension bundle channel, but no matching bundle workload is installed. "
                + "Find one with: func workload search bundles --prerelease");
        }
    }
}
