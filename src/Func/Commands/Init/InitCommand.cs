// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.CommandLine;
using Azure.Functions.Cli.Bundles;
using Azure.Functions.Cli.Commands.Init;
using Azure.Functions.Cli.Commands.Templating;
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

    public Option<string?> LanguageOption { get; } = new("--language", "-l");

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
        IProjectStackConfiguration configuration,
        IFunctionsProjectResolver projectResolver,
        IHostJsonBundleSectionReader bundleSectionReader,
        InstalledBundleScanner bundleScanner,
        InstalledProjectStackCatalog catalog,
        ITemplaterFactory templaterFactory,
        IEnumerable<IProjectInitializer> initializers)
        : base("init", "Initialize a new Azure Functions project.")
    {
        ArgumentNullException.ThrowIfNull(interaction);
        ArgumentNullException.ThrowIfNull(hintRenderer);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(projectResolver);
        ArgumentNullException.ThrowIfNull(bundleSectionReader);
        ArgumentNullException.ThrowIfNull(bundleScanner);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(templaterFactory);
        ArgumentNullException.ThrowIfNull(initializers);

        _initializers = initializers.ToList();
        _workflow = new InitWorkflow(
            interaction,
            hintRenderer,
            configuration,
            catalog,
            projectResolver,
            bundleSectionReader,
            bundleScanner,
            _initializers);
        _templatedInit = new TemplatedInitHandler(_workflow, templaterFactory);
        _legacyInit = new LegacyInitCommand(_workflow);

        StackOption.Description = BuildOptionDescription(
            "The stack to use", _initializers.Select(i => i.Stack.Descriptor.Identifier));
        LanguageOption.Description = BuildOptionDescription(
            "The programming language",
            _initializers.SelectMany(i => i.Stack.SupportedLanguages).Select(l => l.LanguageIdentifier));

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
            StackDescriptor descriptor = initializer.Stack.Descriptor;
            registry.SetActiveStack(descriptor.Identifier);
            IReadOnlyList<Option> options = initializer.GetInitOptions(registry);
            // Bundle options stay general even when only one bundle-using stack is installed.
            RegisterStackOptionHelp(descriptor,
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
        InitRequest request = CreateRequest(parseResult);
        try
        {
            // Every route receives a validated stack and language, so a bad value never leaves files behind.
            InitResolution? resolution = await _workflow.ResolveAsync(request, cancellationToken);
            if (resolution is null)
            {
                return 1;
            }

            if (resolution.Mode != InitMode.Create)
            {
                return await _templatedInit.ReconcileExistingAsync(request, resolution, cancellationToken);
            }

            return await _templatedInit.TryCreateAsync(request, resolution, cancellationToken)
                ?? await _legacyInit.CreateAsync(request, resolution, cancellationToken);
        }
        catch (Exception ex) when (ex is ProjectStackResolutionException or ProjectStackConfigurationException)
        {
            _workflow.Interaction.WriteError(ex.Message);
            return 1;
        }
    }

    private InitRequest CreateRequest(ParseResult parseResult)
        => new(
            parseResult.GetValue(PathArgument!)!,
            parseResult.GetValue(NameOption),
            parseResult.GetValue(StackOption),
            parseResult.GetValue(LanguageOption),
            parseResult.GetValue(ForceOption),
            _stackOptionsCommand,
            GetReservedOptions([]),
            GetTemplateTokens(parseResult, []));

    private static string BuildOptionDescription(string description, IEnumerable<string> values)
    {
        string supported = string.Join(", ",
            values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase));
        return supported.Length == 0
            ? $"{description}. Set up a stack (`func setup --features <id>`) to see supported values."
            : $"{description}. Supported values: {supported}.";
    }
}
