// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Spectre.Console;

namespace Azure.Functions.Cli.Templates;

internal interface INewExecutionRunner
{
    public Task<NewExecutionResult> ExecuteAsync(
        NewExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken);
}

/// <summary>
/// Runs the staged item-template journey against one protected, context-bound native environment.
/// </summary>
internal sealed class NewExecutionRunner(
    INewExecutionContextResolver contexts,
    ITemplaterFactory factory,
    ITemplateArgumentParser arguments,
    IInteractionService interaction) : INewExecutionRunner
{
    private readonly INewExecutionContextResolver _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    private readonly ITemplaterFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    public async Task<NewExecutionResult> ExecuteAsync(
        NewExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(request.RawTemplateTokens);
        ArgumentNullException.ThrowIfNull(request.ReservedAliases);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TemplateReference is not null && string.IsNullOrWhiteSpace(request.TemplateReference))
        {
            return NewExecutionResult.Reject("invalidTemplateReference", "Supply a nonempty template identity or short name.");
        }

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            return NewExecutionResult.Reject("invalidName", "Supply a nonempty --name.");
        }

        if (request.List && (request.RawTemplateTokens.Count > 0 || request.DryRun || request.Force || request.Name is not null))
        {
            return NewExecutionResult.Reject("invalidListArguments", "Use --list without template arguments, --name, --force or --dry-run.");
        }

        bool canPrompt = !request.NonInteractive && _interaction.IsInteractive;
        NewExecutionContextResult resolved = await _contexts.ResolveAsync(request, canPrompt, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        TemplateEngineContext context = resolved.Context ?? throw new InvalidOperationException("Context resolution returned no outcome.");
        context = context with { RequireCleanStore = request.DryRun || request.List };
        using Templater templater = await _factory.CreateAsync(context, availability, cancellationToken,
            _ => _interaction.WriteWarning("Waiting for the installed template store to become available."));
        cancellationToken.ThrowIfCancellationRequested();
        NewExecutionResult result = await ExecuteAsync(templater, request, canPrompt, cancellationToken);
        if (request.DryRun || !result.HasCreationOutcome)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result with { Context = context };
    }

    private async Task<NewExecutionResult> ExecuteAsync(
        Templater templater, NewExecutionRequest request, bool canPrompt, CancellationToken cancellationToken)
    {
        string language = templater.Context.Project!.Language!;
        List<TemplateGroup> groups = [];
        List<TemplateCandidateParseResult> parsed = [];
        List<TemplateCatalogEntry> considered = [];
        if (request.TemplateReference is null || request.List)
        {
            IReadOnlyList<TemplateCatalogEntry> catalog = await templater.ListAsync(TemplateType.Item, cancellationToken);
            considered.AddRange(catalog.Where(entry => !entry.IsHidden));
            TemplateCatalogEntry[] visible = [.. catalog.Where(entry => !entry.IsHidden
                && entry.MetadataDiagnostic is null && entry.ConstraintEvaluation.Status == TemplateConstraintEvaluationStatus.Eligible
                && string.Equals(entry.Language, language, StringComparison.OrdinalIgnoreCase))];
            if (request.List)
            {
                if (request.TemplateReference is { } reference)
                {
                    visible = [.. visible.Where(entry => string.Equals(entry.Identity, reference, StringComparison.Ordinal)
                        || entry.ShortNames.Contains(reference, StringComparer.OrdinalIgnoreCase))];
                }

                return new("listed", true) { Templates = Array.AsReadOnly(visible) };
            }

            if (!canPrompt)
            {
                return NewExecutionResult.Reject("templateRequired", "Supply <template> or --template <identity-or-short-name>; this terminal cannot prompt.");
            }

            foreach (IGrouping<string, TemplateCatalogEntry> family in visible.GroupBy(GroupIdentity, StringComparer.Ordinal))
            {
                TemplateResolutionResult group = await templater.CreateGroupAsync([.. family], cancellationToken);
                AddCompatible(group.Group!);
            }
        }
        else
        {
            TemplateResolutionResult resolution = await templater.ResolveGroupAsync(request.TemplateReference, TemplateType.Item, cancellationToken);
            switch (resolution.Status)
            {
                case TemplateResolutionStatus.Success:
                    AddCompatible(resolution.Group!);
                    break;
                case TemplateResolutionStatus.AmbiguousGroup:
                    foreach (IGrouping<string, TemplateCatalogEntry> family in resolution.Diagnostics
                        .Where(entry => entry.Type == TemplateType.Item).GroupBy(GroupIdentity, StringComparer.Ordinal))
                    {
                        TemplateResolutionResult group = await templater.CreateGroupAsync([.. family], cancellationToken);
                        AddCompatible(group.Group!);
                    }

                    break;
                default:
                    string diagnostic = resolution.Status switch
                    {
                        TemplateResolutionStatus.NotFound => $"No installed item template matches '{request.TemplateReference}'. Use --list to inspect installed items.",
                        TemplateResolutionStatus.WrongType => $"'{request.TemplateReference}' is not an item template. Use 'func init' for project templates.",
                        TemplateResolutionStatus.InvalidTemplateType => resolution.Diagnostic!,
                        TemplateResolutionStatus.Unavailable => $"'{request.TemplateReference}' is unavailable in this project. Inspect its constraint and metadata diagnostics.",
                        _ => throw new InvalidOperationException($"Unknown template resolution: {resolution.Status}"),
                    };
                    return NewExecutionResult.Reject(resolution.Status.ToString(), diagnostic) with { Templates = resolution.Diagnostics };
            }
        }

        if (groups.Count == 0)
        {
            return NewExecutionResult.Reject(parsed.Count > 0 ? "invalidArguments" : "noCompatibleTemplates",
                parsed.Count > 0
                    ? "No item candidate accepts these template arguments. Correct the reported aliases and values."
                    : $"No eligible installed item matches language '{language}'. Available languages: " +
                        $"{string.Join(", ", considered.Select(entry => entry.Language).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))}. " +
                        "Inspect the installed items or supply a supported --language.") with
            {
                ArgumentResults = parsed.AsReadOnly(),
                Templates = parsed.Count == 0 ? considered.AsReadOnly() : [],
            };
        }

        TemplateGroup selectedGroup;
        if (groups.Count > 1 || request.TemplateReference is null)
        {
            if (!canPrompt)
            {
                return NewExecutionResult.Reject("ambiguousGroups", "Multiple template families remain. Supply --template with an exact identity.") with
                {
                    Templates = Array.AsReadOnly<TemplateCatalogEntry>([.. groups.SelectMany(group => group).Select(template => template.Entry)]),
                    ArgumentResults = parsed.AsReadOnly(),
                };
            }

            selectedGroup = await PickAsync("Select an item-template family:", groups, group => GroupIdentity(group[0].Entry), cancellationToken);
        }
        else
        {
            selectedGroup = groups[0];
        }

        TemplateGroup narrowed = selectedGroup.FilterToHighestPrecedence();
        ResolvedTemplate selected;
        if (narrowed.Count > 1)
        {
            if (!canPrompt)
            {
                return NewExecutionResult.Reject("ambiguousCandidates", "Multiple item candidates remain. Supply --template with an exact identity.") with
                {
                    Templates = Array.AsReadOnly<TemplateCatalogEntry>([.. narrowed.Select(template => template.Entry)]),
                    ArgumentResults = parsed.AsReadOnly(),
                };
            }

            selected = await PickAsync("Select an item-template candidate:", narrowed, template => template.Entry.Identity, cancellationToken);
        }
        else
        {
            selected = narrowed[0];
        }

        TemplateCandidateParseResult initial = parsed.Single(result => result.TemplateIdentity == selected.Entry.Identity);
        if (initial.MissingRequiredParameters.Count > 0 && !canPrompt)
        {
            return NewExecutionResult.Reject("missingRequired",
                $"Supply all required template values: {string.Join(", ", initial.MissingRequiredParameters.Select(parameter => parameter.LongAlias))}.") with
            {
                SelectedTemplate = selected.Entry,
                ArgumentResults = [initial],
            };
        }

        Dictionary<string, string?> completed = new(StringComparer.Ordinal);
        foreach (TemplateArgumentParameter parameter in initial.MissingRequiredParameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TemplateParameterDefinition definition = parameter.Definition;
            string label = $"{parameter.LongAlias} ({definition.DataType})";
            string value;
            if (definition.Choices.Count > 0)
            {
                value = await PromptForLiteralSelectionAsync(label, definition.Choices.Select(choice => choice.Value), cancellationToken);
            }
            else if (definition.DataType.Equals("bool", StringComparison.OrdinalIgnoreCase)
                || definition.DataType.Equals("boolean", StringComparison.OrdinalIgnoreCase))
            {
                value = await PromptForLiteralSelectionAsync(label, ["true", "false"], cancellationToken);
            }
            else
            {
                value = await _interaction.PromptForInputAsync(Markup.Escape(label), cancellationToken: cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            completed.Add(definition.CanonicalName, value);
        }

        TemplateCandidateParseResult final = _arguments.Parse(new(
            selected.Entry.Identity, selected.Parameters, request.ReservedAliases, request.RawTemplateTokens, completed), cancellationToken);
        if (!final.IsComplete)
        {
            return NewExecutionResult.Reject("invalidCompletion", "Required template values are still invalid or unresolved.") with
            {
                SelectedTemplate = selected.Entry,
                ArgumentResults = [final],
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult invocation = await selected.InvokeAsync(new(
            request.WorkingDirectory, final.ValuesByCanonicalName, request.Name, request.DryRun, request.Force), cancellationToken);
        bool success = invocation.Status == TemplateInvocationStatus.Success;
        NewExecutionResult result = new(success ? request.DryRun ? "previewed" : "created" : "invocationFailed", success)
        {
            SelectedTemplate = selected.Entry,
            ArgumentResults = [final],
            Invocation = invocation,
            Diagnostics = invocation.Diagnostic is null ? [] : [invocation.Diagnostic],
        };
        if (request.DryRun || !result.HasCreationOutcome)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result;

        void AddCompatible(TemplateGroup group)
        {
            considered.AddRange(group.Select(template => template.Entry));
            TemplateGroup matchingLanguage = group.FilterByLanguage(language);
            TemplateCandidateParseResult[] results = [.. matchingLanguage.Select(template => _arguments.Parse(new(
                template.Entry.Identity, template.Parameters, request.ReservedAliases, request.RawTemplateTokens), cancellationToken))];
            parsed.AddRange(results);
            TemplateGroup compatible = matchingLanguage.FilterByArguments(
                results.Where(result => result.IsArgumentCompatible).Select(result => result.TemplateIdentity).ToHashSet(StringComparer.Ordinal));
            if (compatible.Count > 0)
            {
                groups.Add(compatible);
            }
        }
    }

    private async Task<T> PickAsync<T>(string title, IReadOnlyList<T> choices, Func<T, string> label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var byLabel = choices.ToDictionary(label, StringComparer.Ordinal);
        string selected = await PromptForLiteralSelectionAsync(title, byLabel.Keys, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return byLabel.TryGetValue(selected, out T? value)
            ? value
            : throw new InvalidOperationException("The interaction service returned a choice that was not offered.");
    }

    private async Task<string> PromptForLiteralSelectionAsync(string title, IEnumerable<string> choices, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rawByLabel = choices.ToDictionary(Markup.Escape, StringComparer.Ordinal);
        string selected = await _interaction.PromptForSelectionAsync(Markup.Escape(title), rawByLabel.Keys, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return rawByLabel.TryGetValue(selected, out string? raw) ? raw : selected;
    }

    private static string GroupIdentity(TemplateCatalogEntry entry) => entry.GroupIdentity ?? entry.Identity;
}
