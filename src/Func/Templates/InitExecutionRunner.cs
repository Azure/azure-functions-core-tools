// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Text.Json;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Configuration;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Projects;
using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

internal interface IInitExecutionRunner
{
    public Task<InitExecutionResult> ExecuteAsync(
        InitExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken);
}

/// <summary>
/// Composes installed project templates and mandatory configuration in one protected invocation.
/// </summary>
internal sealed class InitExecutionRunner(
    ITemplaterFactory factory,
    ITemplateArgumentParser arguments,
    TemplateExecutionInteraction prompts,
    IInteractionService interaction,
    InitExecutionRenderer renderer,
    InitExecutionTargetInspector targets,
    Func<InstalledProjectStackCatalog> stackCatalog,
    Func<TemplateProjectSupport> projectSupport,
    IFileSystem fileSystem,
    ILocalSettingsProvider localSettings,
    IFunctionsProjectResolver projects) : IInitExecutionRunner
{
    private readonly ITemplaterFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly TemplateExecutionInteraction _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly InitExecutionRenderer _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    private readonly InitExecutionTargetInspector _targets = targets ?? throw new ArgumentNullException(nameof(targets));
    private readonly Func<InstalledProjectStackCatalog> _stackCatalog = stackCatalog ?? throw new ArgumentNullException(nameof(stackCatalog));
    private readonly Func<TemplateProjectSupport> _projectSupport = projectSupport ?? throw new ArgumentNullException(nameof(projectSupport));
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly ILocalSettingsProvider _localSettings = localSettings ?? throw new ArgumentNullException(nameof(localSettings));
    private readonly IFunctionsProjectResolver _projects = projects ?? throw new ArgumentNullException(nameof(projects));

    public async Task<InitExecutionResult> ExecuteAsync(
        InitExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(request.WorkingDirectory);
        ArgumentNullException.ThrowIfNull(request.RawTemplateTokens);
        ArgumentNullException.ThrowIfNull(request.ReservedAliases);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TemplateReference is not null && string.IsNullOrWhiteSpace(request.TemplateReference))
        {
            return InitExecutionResult.Reject("invalidTemplateReference", "Supply a nonempty --template identity or short name.");
        }

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            return InitExecutionResult.Reject("invalidName", "Supply a nonempty --name.");
        }

        if (request.Force)
        {
            return InitExecutionResult.Reject("forceNotQualified",
                "Force cleanup is not qualified by this staged INIT consumer. Nothing was deleted; use a new empty target.");
        }

        InitExecutionTarget target;
        try
        {
            target = await _targets.InspectAsync(request.WorkingDirectory, cancellationToken);
        }
        catch (JsonException exception)
        {
            return InitExecutionResult.Reject("invalidConfiguration",
                $"Cannot read '.func/config.json'. Correct its JSON and stack fields before retrying. {exception.Message}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return InitExecutionResult.Reject("invalidTarget", $"Cannot inspect the target directory or configuration. Check the path and access, then retry. {exception.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (target.State == InitExecutionTargetState.Unsupported)
        {
            return InitExecutionResult.Reject("unsupportedTarget", target.Diagnostic!);
        }

        InstalledProjectStackCatalog catalog;
        try
        {
            catalog = _stackCatalog();
        }
        catch (ConflictingProjectStackMetadataException exception)
        {
            return InitExecutionResult.Reject("duplicateStacks",
                $"Installed stack metadata conflicts. {exception.Message} Inspect 'func workload list' and remove the conflict.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        bool repair = target.State == InitExecutionTargetState.Configured
            && target.Runtime is not null && target.Language is null
            && catalog.FindStack(target.Runtime) is { SupportedLanguages.Count: > 1 };
        if (target.State == InitExecutionTargetState.Configured && !repair)
        {
            return InitExecutionResult.Reject("alreadyInitialized",
                "This directory already contains Functions CLI configuration. Nothing was changed; use a new target for project-template creation.");
        }

        bool existing = target.State == InitExecutionTargetState.Adoptable || repair;
        if (existing && request.TemplateReference is not null)
        {
            return InitExecutionResult.Reject("templateOnExistingProject",
                "--template cannot be used during adoption or repair. Reinitialization requires --force, which is not qualified by this staged consumer; nothing was changed.");
        }

        if (existing && (request.RawTemplateTokens.Count > 0 || request.Name is not null))
        {
            return InitExecutionResult.Reject("invalidExistingProjectArguments",
                "Adoption and repair only update CLI configuration. Remove --name and template-specific arguments; no template is loaded.");
        }

        if (catalog.Stacks.Count == 0)
        {
            return InitExecutionResult.Reject("noInstalledStacks", "Set up a stack with 'func setup --features <stack>' before initializing a project.");
        }

        IProjectStack? requestedStack = string.IsNullOrWhiteSpace(request.ExplicitStack) ? null : catalog.FindStack(request.ExplicitStack);
        if (request.ExplicitStack is not null && requestedStack is null)
        {
            return InitExecutionResult.Reject("invalidStack",
                $"Stack '{request.ExplicitStack}' must identify one installed stack. Supported stacks: {string.Join(", ", catalog.Stacks.Select(stack => stack.Stack))}.");
        }

        string? requestedLanguage = string.IsNullOrWhiteSpace(request.ExplicitLanguage) ? null : catalog.GetCanonicalLanguage(request.ExplicitLanguage);
        if (request.ExplicitLanguage is not null && (requestedLanguage is null
            || (requestedStack is not null && !catalog.GetStacksForLanguage(request.ExplicitLanguage)
                .Contains(requestedStack))))
        {
            return InitExecutionResult.Reject("invalidLanguage",
                $"Language '{request.ExplicitLanguage}' does not match the installed stack selection. Supported languages: " +
                $"{string.Join(", ", (requestedStack is null ? catalog.Stacks : [requestedStack])
                    .SelectMany(stack => stack.SupportedLanguages).Distinct(StringComparer.OrdinalIgnoreCase))}.");
        }

        if (existing)
        {
            return await ConfigureExistingAsync(request, target, repair, catalog, requestedStack, requestedLanguage, cancellationToken);
        }

        TemplateEngineContext context = new(request.WorkingDirectory, new(request.WorkingDirectory, stack: null));
        cancellationToken.ThrowIfCancellationRequested();
        TemplateProjectSupport support = _projectSupport();
        cancellationToken.ThrowIfCancellationRequested();
        using Templater templater = await _factory.CreateProjectAsync(context, availability, support, cancellationToken,
            _ => _interaction.WriteWarning("Waiting for the installed template store to become available."));
        cancellationToken.ThrowIfCancellationRequested();
        InitExecutionResult result = await ExecuteAsync(templater, request, Matches, cancellationToken);
        if (!HasConfirmedCreationResult(result, request.DryRun))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result with { Context = context };

        bool Matches(string stack, string language) =>
            (requestedStack is null || string.Equals(stack, requestedStack.Stack, StringComparison.OrdinalIgnoreCase))
            && (requestedLanguage is null || string.Equals(language, requestedLanguage, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<InitExecutionResult> ConfigureExistingAsync(InitExecutionRequest request, InitExecutionTarget target,
        bool repair, InstalledProjectStackCatalog catalog, IProjectStack? requestedStack, string? requestedLanguage,
        CancellationToken cancellationToken)
    {
        bool canPrompt = !request.NonInteractive && _interaction.IsInteractive;
        string? runtime = repair ? target.Runtime : _localSettings.Get(request.WorkingDirectory.Info).WorkerRuntime?.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        IProjectStack? declared = string.IsNullOrWhiteSpace(runtime) ? null : catalog.FindStack(runtime);
        IProjectStack? selected = requestedStack ?? declared;
        if (selected is not null && declared is not null && !string.Equals(selected.Stack, declared.Stack, StringComparison.OrdinalIgnoreCase))
        {
            return InitExecutionResult.Reject("projectStackConflict",
                $"--stack '{request.ExplicitStack}' conflicts with the project's runtime '{runtime}'. Supply the matching stack; nothing was changed.");
        }

        if (selected is null && !string.IsNullOrWhiteSpace(runtime))
        {
            return InitExecutionResult.Reject("uninstalledProjectStack",
                $"The project's runtime '{runtime}' has no installed stack. Run 'func setup --features <stack>' and retry.");
        }

        if (selected is null)
        {
            if (!canPrompt)
            {
                return InitExecutionResult.Reject("missingProjectStack",
                    $"Couldn't detect the project's stack from local.settings.json. Supply --stack <{string.Join("|", catalog.Stacks.Select(stack => stack.Stack))}>; adoption never guesses a stack.");
            }

            selected = await _prompts.PickAsync("Adopting an existing project. Which stack is it?", catalog.Stacks,
                stack => $"{stack.DisplayName} ({stack.Stack})", cancellationToken);
        }

        string? language = requestedLanguage;
        cancellationToken.ThrowIfCancellationRequested();
        ProjectResolutionResult resolution;
        try
        {
            resolution = await _projects.ResolveProjectAsync(new(request.WorkingDirectory), cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return InitExecutionResult.Reject("invalidProjectMetadata",
                $"Cannot read existing project metadata. Check the project files and access, then retry. {exception.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (resolution is ProjectResolutionResult.Resolved resolved)
        {
            if (!string.Equals(resolved.Project.StackName, selected.Stack, StringComparison.OrdinalIgnoreCase))
            {
                return InitExecutionResult.Reject("projectStackConflict",
                    $"Existing files identify stack '{resolved.Project.StackName}', not '{selected.Stack}'. Supply matching project metadata; nothing was changed.");
            }

            language ??= resolved.Project.Language;
        }
        else if (resolution is ProjectResolutionResult.NotResolved { Failure: not null } failed)
        {
            return InitExecutionResult.Reject("invalidProjectMetadata", failed.Message);
        }

        string? canonicalLanguage = string.IsNullOrWhiteSpace(language) ? null : catalog.GetCanonicalLanguage(language);
        if (language is not null && (canonicalLanguage is null
            || !selected.SupportedLanguages.Contains(canonicalLanguage, StringComparer.OrdinalIgnoreCase)))
        {
            return InitExecutionResult.Reject("invalidProjectLanguage",
                $"Language '{language ?? "(unavailable)"}' is not supported by stack '{selected.Stack}'. Supply --language with one of: {string.Join(", ", selected.SupportedLanguages)}.");
        }

        IReadOnlyList<string> supported = selected.SupportedLanguages;
        if (canonicalLanguage is null && supported.Count > 1 && !canPrompt)
        {
            return InitExecutionResult.Reject("missingProjectLanguage",
                $"Couldn't resolve the existing project's language. Supply --language <{string.Join("|", supported)}>; no configuration was written.");
        }

        language = canonicalLanguage ?? (supported.Count == 1 ? supported[0]
            : await _prompts.PickAsync("Select the existing project's language:", supported, value => value, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        InitExistingProjectConfiguration configuration = new(request.WorkingDirectory.Info.FullName, selected.Stack, language, repair);
        if (request.DryRun)
        {
            return new(repair ? "repairPreviewed" : "adoptionPreviewed", true) { Configuration = configuration };
        }

        try
        {
            string json;
            if (repair)
            {
                InitExecutionTarget current = await _targets.InspectAsync(request.WorkingDirectory, cancellationToken);
                if (current.State != InitExecutionTargetState.Configured || current.Runtime is null
                    || catalog.FindStack(current.Runtime) != selected
                    || current.Language is not null)
                {
                    return InitExecutionResult.Reject("targetChanged", "Project configuration changed during repair. Inspect it and retry; nothing was written.")
                        with { Configuration = configuration };
                }

                json = ProjectConfigurationJson.MergeLanguage(current.ConfigurationJson!, language, selected.Stack);
            }
            else
            {
                if (_fileSystem.FileExists(configuration.Path))
                {
                    return InitExecutionResult.Reject("targetChanged", "Project configuration appeared during adoption. Inspect it and retry; the existing file was not replaced.")
                        with { Configuration = configuration };
                }

                json = ProjectConfigurationJson.Create(selected.Stack, language);
            }

            cancellationToken.ThrowIfCancellationRequested();
            await _fileSystem.WriteAllTextAsync(configuration.Path, json + Environment.NewLine, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return InitExecutionResult.Reject("configurationFailed",
                $"Could not {(repair ? "repair" : "create")} '.func/config.json'. Check the input JSON, path and write access, then retry. The project was not completed. {exception.Message}")
                with { Configuration = configuration };
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(repair ? "repaired" : "adopted", true) { Configuration = configuration with { Written = true } };
    }

    private async Task<InitExecutionResult> ExecuteAsync(Templater templater, InitExecutionRequest request,
        Func<string, string, bool> matches, CancellationToken cancellationToken)
    {
        bool canPrompt = !request.NonInteractive && _interaction.IsInteractive;
        List<TemplateGroup> groups = [];
        List<TemplateCandidateParseResult> parsed = [];
        List<TemplateCatalogEntry> considered = [];
        if (request.TemplateReference is null)
        {
            IReadOnlyList<TemplateCatalogEntry> catalog = await templater.ListAsync(TemplateType.Project, cancellationToken);
            considered.AddRange(catalog.Where(entry => !entry.IsHidden && CanMatch(entry)));
            foreach (IGrouping<string, TemplateCatalogEntry> family in considered.Where(IsUsable).GroupBy(GroupIdentity, StringComparer.Ordinal))
            {
                TemplateResolutionResult resolution = await templater.CreateGroupAsync([.. family], cancellationToken);
                AddSelectable(resolution.Group!);
            }
        }
        else
        {
            TemplateResolutionResult resolution = await templater.ResolveGroupAsync(request.TemplateReference, TemplateType.Project, cancellationToken);
            considered.AddRange(resolution.Diagnostics);
            switch (resolution.Status)
            {
                case TemplateResolutionStatus.Success:
                    considered.AddRange(resolution.Group!.Select(template => template.Entry).Except(considered));
                    AddSelectable(resolution.Group!);
                    break;
                case TemplateResolutionStatus.AmbiguousGroup:
                    foreach (IGrouping<string, TemplateCatalogEntry> family in resolution.Diagnostics.Where(entry => entry.Type == TemplateType.Project)
                        .GroupBy(GroupIdentity, StringComparer.Ordinal))
                    {
                        TemplateResolutionResult group = await templater.CreateGroupAsync([.. family], cancellationToken);
                        AddSelectable(group.Group!);
                    }

                    break;
                default:
                    return InitExecutionResult.Reject(resolution.Status.ToString(), resolution.Status switch
                    {
                        TemplateResolutionStatus.NotFound => $"Template '{request.TemplateReference}' is not installed. {PackageGuidance}",
                        TemplateResolutionStatus.WrongType => $"'{request.TemplateReference}' is not a project template. Use 'func new' for item templates.",
                        TemplateResolutionStatus.InvalidTemplateType => resolution.Diagnostic!,
                        TemplateResolutionStatus.Unavailable => $"Template '{request.TemplateReference}' is unavailable. Inspect its metadata and constraint diagnostics.",
                        _ => throw new InvalidOperationException($"Unknown template resolution: {resolution.Status}"),
                    }) with { Templates = considered.AsReadOnly() };
            }
        }

        TemplateCatalogEntry[] unavailable = [.. considered.Where(entry => !IsUsable(entry) && CanMatch(entry))];
        if (groups.Count == 0)
        {
            return InitExecutionResult.Reject("noCompatibleTemplates",
                $"No eligible installed project template matches --stack '{request.ExplicitStack ?? "(any)"}' and --language '{request.ExplicitLanguage ?? "(any)"}'. {PackageGuidance}") with
            {
                Templates = considered.AsReadOnly(),
                ArgumentResults = parsed.AsReadOnly(),
                UnavailableTemplates = unavailable,
            };
        }

        TemplateGroup selectedGroup;
        if (groups.Count > 1 || (request.TemplateReference is null && canPrompt && unavailable.Length > 0))
        {
            if (!canPrompt)
            {
                return InitExecutionResult.Reject("ambiguousGroups", "Multiple project-template families remain. Supply --template with an exact identity.") with
                {
                    Templates = Array.AsReadOnly<TemplateCatalogEntry>([.. groups.SelectMany(group => group).Select(template => template.Entry)]),
                    ArgumentResults = parsed.AsReadOnly(),
                    UnavailableTemplates = unavailable,
                };
            }

            if (unavailable.Length > 0 && !request.JsonOutput)
            {
                _renderer.RenderUnavailable(unavailable);
            }

            selectedGroup = await _prompts.PickAsync("Select a project-template family:", groups,
                group => GroupIdentity(group[0].Entry), cancellationToken);
        }
        else
        {
            selectedGroup = groups[0];
        }

        parsed.AddRange(selectedGroup.Select(template => _arguments.Parse(new(template.Entry.Identity, template.Parameters,
            request.ReservedAliases, request.RawTemplateTokens), cancellationToken)));
        TemplateGroup compatible = selectedGroup.FilterByArguments(parsed.Where(result => result.IsArgumentCompatible)
            .Select(result => result.TemplateIdentity).ToHashSet(StringComparer.Ordinal));
        if (compatible.Count == 0)
        {
            return InitExecutionResult.Reject("invalidArguments",
                "No project candidate accepts these template arguments. Correct the reported aliases and values.") with
            {
                ArgumentResults = parsed.AsReadOnly(),
                UnavailableTemplates = unavailable,
            };
        }

        TemplateGroup narrowed = compatible.FilterToHighestPrecedence();
        ResolvedTemplate selected;
        if (narrowed.Count > 1)
        {
            if (!canPrompt)
            {
                return InitExecutionResult.Reject("ambiguousCandidates", "Multiple project variants remain. Supply --template with an exact identity.") with
                {
                    Templates = Array.AsReadOnly<TemplateCatalogEntry>([.. narrowed.Select(template => template.Entry)]),
                    ArgumentResults = parsed.AsReadOnly(),
                    UnavailableTemplates = unavailable,
                };
            }

            selected = await _prompts.PickAsync("Select a project-template variant:", narrowed, template => template.Entry.Identity, cancellationToken);
        }
        else
        {
            selected = narrowed[0];
        }

        TemplateCandidateParseResult initial = parsed.Single(result => result.TemplateIdentity == selected.Entry.Identity);
        if (initial.MissingRequiredParameters.Count > 0 && !canPrompt)
        {
            return InitExecutionResult.Reject("missingRequired",
                $"Supply all required template values: {string.Join(", ", initial.MissingRequiredParameters.Select(parameter => parameter.LongAlias))}.") with
            {
                SelectedTemplate = selected.Entry,
                ArgumentResults = [initial],
                UnavailableTemplates = unavailable,
            };
        }

        TemplateCandidateParseResult final = await _prompts.CompleteAsync(
            selected, initial, request.ReservedAliases, request.RawTemplateTokens, cancellationToken);
        if (!final.IsComplete)
        {
            return InitExecutionResult.Reject("invalidCompletion", "Required template values are still invalid or unresolved.") with
            {
                SelectedTemplate = selected.Entry,
                ArgumentResults = [final],
                UnavailableTemplates = unavailable,
            };
        }

        TemplateInvocationRequest invocationRequest = new(request.WorkingDirectory, final.ValuesByCanonicalName, request.Name, DryRun: true);
        TemplateInvocationResult preview = await selected.InvokeAsync(invocationRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (preview.Status != TemplateInvocationStatus.Success)
        {
            return FromPreview("invocationFailed");
        }

        if (preview.ProjectConfigurations.Count == 0 || preview.ProjectConfigurations.Any(plan => !matches(plan.Stack, plan.Language)))
        {
            return InitExecutionResult.Reject("projectFilterConflict",
                "Resolved project configurations do not satisfy every explicit stack/language filter. No project files were written.") with
            {
                SelectedTemplate = selected.Entry,
                ArgumentResults = [final],
                Preview = preview,
                UnavailableTemplates = unavailable,
            };
        }

        if (request.DryRun)
        {
            return FromPreview("previewed");
        }

        InitExecutionResult? targetFailure = CheckTarget(request.WorkingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        if (targetFailure is not null)
        {
            return targetFailure with
            {
                SelectedTemplate = selected.Entry,
                ArgumentResults = [final],
                Preview = preview,
                UnavailableTemplates = unavailable,
            };
        }

        TemplateInvocationResult created = await selected.InvokeAsync(invocationRequest with { DryRun = false }, cancellationToken);
        InitExecutionResult creation = FromInvocation(created, created.Status == TemplateInvocationStatus.Success ? "created"
            : created.Status == TemplateInvocationStatus.ConfigurationFailure ? "partialInitialization" : "invocationFailed");
        if (!HasConfirmedCreationResult(creation, request.DryRun))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return creation;

        InitExecutionResult FromInvocation(TemplateInvocationResult invocation, string kind) => new(kind, invocation.Status == TemplateInvocationStatus.Success)
        {
            SelectedTemplate = selected.Entry,
            ArgumentResults = [final],
            Invocation = invocation,
            Preview = preview,
            Diagnostics = invocation.Diagnostic is null ? [] : [invocation.Diagnostic],
            UnavailableTemplates = unavailable,
        };

        InitExecutionResult FromPreview(string kind) => new(kind, preview.Status == TemplateInvocationStatus.Success)
        {
            SelectedTemplate = selected.Entry,
            ArgumentResults = [final],
            Preview = preview,
            Diagnostics = preview.Diagnostic is null ? [] : [preview.Diagnostic],
            UnavailableTemplates = unavailable,
        };

        bool CanMatch(TemplateCatalogEntry entry) => entry.ProjectConfigurations is not { } declarations
            || (declarations.Count > 0 && declarations.All(declaration => matches(declaration.Stack, declaration.Language)));

        void AddSelectable(TemplateGroup group)
        {
            TemplateGroup selectable = group.FilterByArguments(group.Where(template => IsUsable(template.Entry) && CanMatch(template.Entry))
                .Select(template => template.Entry.Identity).ToHashSet(StringComparer.Ordinal));
            if (selectable.Count > 0)
            {
                groups.Add(selectable);
            }
        }
    }

    internal static bool HasConfirmedCreationResult(InitExecutionResult result, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(result);
        return !dryRun && (result is { Kind: "created", Succeeded: true, Invocation.Status: TemplateInvocationStatus.Success }
            or { Kind: "partialInitialization", Succeeded: false, Invocation.Status: TemplateInvocationStatus.ConfigurationFailure });
    }

    private const string PackageGuidance =
        "Use the template author's package instructions and install explicitly with 'func new install <package>'; no package is installed automatically.";

    private InitExecutionResult? CheckTarget(WorkingDirectory target)
    {
        string? diagnostic;
        try
        {
            diagnostic = _targets.GetDiagnostic(target);
        }
        catch (IOException exception)
        {
            return InitExecutionResult.Reject("invalidTarget", $"Cannot inspect the target directory. Check the path and retry. {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return InitExecutionResult.Reject("invalidTarget", $"Cannot inspect the target directory. Check access and retry. {exception.Message}");
        }

        return diagnostic is null ? null : InitExecutionResult.Reject("unsupportedTarget", diagnostic);
    }

    private static bool IsUsable(TemplateCatalogEntry entry) => entry.MetadataDiagnostic is null
        && entry.ProjectConfigurationDiagnostic is null && entry.ProjectConfigurations is { Count: > 0 }
        && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible;

    private static string GroupIdentity(TemplateCatalogEntry entry) => entry.GroupIdentity ?? entry.Identity;
}
