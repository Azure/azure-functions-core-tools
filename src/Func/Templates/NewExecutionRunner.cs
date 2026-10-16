// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.ObjectModel;
using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;

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
    IInteractionService interaction,
    TemplateExecutionInteraction prompts,
    NativeTemplateWorkflowRegistry? workflows = null) : INewExecutionRunner
{
    private readonly INewExecutionContextResolver _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
    private readonly ITemplaterFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ITemplateArgumentParser _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
    private readonly TemplateExecutionInteraction _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
    private readonly NativeTemplateWorkflowRegistry? _workflows = workflows;

    public async Task<NewExecutionResult> ExecuteAsync(
        NewExecutionRequest request, TemplateConstraintEvaluationSnapshot availability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(request.RawTemplateTokens);
        ArgumentNullException.ThrowIfNull(request.ReservedAliases);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.OutputIntent is not (NativeItemOutputIntent.LiteralOutput or NativeItemOutputIntent.LegacyWorkflowRelative))
        {
            return NewExecutionResult.Reject("invalidOutputIntent", "The item invocation intent is not supported.");
        }
        if (request.TemplateReference is not null && string.IsNullOrWhiteSpace(request.TemplateReference))
        {
            return NewExecutionResult.Reject("invalidTemplateReference", "Supply a nonempty template identity or short name.");
        }

        if (request.Name is not null && string.IsNullOrWhiteSpace(request.Name))
        {
            return NewExecutionResult.Reject("invalidName", "Supply a nonempty --name.");
        }

        if (request.List && (request.RawTemplateTokens.Count > 0 || request.DryRun))
        {
            return NewExecutionResult.Reject("invalidListArguments", "Use --list without template arguments or --dry-run.");
        }

        bool canPrompt = !request.NonInteractive && _interaction.IsInteractive;
        NewExecutionContextResult resolved = await _contexts.ResolveAsync(request, canPrompt, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolved.Failure is { } failure)
        {
            return failure;
        }

        TemplateEngineContext context = resolved.Context ?? throw new InvalidOperationException("Context resolution returned no outcome.");
        using Templater templater = await _factory.CreateAsync(context, availability, cancellationToken,
            _ => _interaction.WriteWarning("Waiting for the installed template store to become available."));
        cancellationToken.ThrowIfCancellationRequested();
        NewExecutionResult result = await ExecuteAsync(templater, request, canPrompt, cancellationToken);
        if (request.DryRun || !result.HasCompletedCreation)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result with { Context = context };
    }

    private async Task<NewExecutionResult> ExecuteAsync(
        Templater templater, NewExecutionRequest request, bool canPrompt, CancellationToken cancellationToken)
    {
        string language = templater.Context.Project!.Language!;
        INativeItemWorkflowProvider? provider = _workflows?.FindItem(templater.Context.Project.Stack!);
        if (_workflows is not null && provider is null)
        {
            return NewExecutionResult.Reject("workflowUnavailable", "No CLI-owned native item workflow is composed for the selected installed stack.");
        }

        INativeItemCompatibility? compatibility = provider?.Compatibility ?? (_workflows is null ? request.ItemCompatibility : null);
        INativeSelectedItemEffects? selectedEffects = provider?.Effects ?? (_workflows is null ? request.SelectedItemEffects : null);
        List<TemplateGroup> groups = [];
        List<TemplateCandidateParseResult> parsed = [];
        List<TemplateCatalogEntry> considered = [];
        if (request.TemplateReference is null || request.List)
        {
            IReadOnlyList<TemplateCatalogEntry> catalog = await templater.ListAsync(TemplateType.Item, cancellationToken);
            considered.AddRange(catalog.Where(entry => !entry.IsHidden));
            TemplateCatalogEntry[] visible = [.. catalog.Where(entry => !entry.IsHidden && entry.DeclaredInternalRole is null
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

            selectedGroup = await _prompts.PickAsync("Select an item-template family:", groups, group => GroupIdentity(group[0].Entry), cancellationToken);
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

            selected = await _prompts.PickAsync("Select an item-template candidate:", narrowed, template => template.Entry.Identity, cancellationToken);
        }
        else
        {
            selected = narrowed[0];
        }

        if (selected.Entry.DeclaredInternalRole is not null)
        {
            return NewExecutionResult.Reject("internalTemplate",
                "This item declares an internal role and cannot be invoked as an ordinary function template.") with { SelectedTemplate = selected.Entry };
        }

        NativeSelectedItemBinding? effectBinding = null;
        if (selectedEffects is { } effectSupport)
        {
            effectBinding = await effectSupport.ResolveAsync(selected, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (effectBinding.InternalExpansionOnly || effectBinding.Diagnostics.Count > 0)
            {
                return NewExecutionResult.Reject("internalTemplate",
                    effectBinding.InternalExpansionOnly ? ["This item is an internal expansion helper, not an ordinary function template."]
                        : [.. effectBinding.Diagnostics]) with { SelectedTemplate = selected.Entry };
            }
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

        TemplateCandidateParseResult final = await _prompts.CompleteAsync(
            selected, initial, request.ReservedAliases, request.RawTemplateTokens, cancellationToken);
        if (!final.IsComplete)
        {
            return NewExecutionResult.Reject("invalidCompletion", "Required template values are still invalid or unresolved.") with
            {
                SelectedTemplate = selected.Entry,
                ArgumentResults = [final],
            };
        }

        NativeItemCompatibilityResult? itemName = null;
        if (compatibility is not null)
        {
            itemName = await compatibility.ResolveAsync(selected, final, request.Name, canPrompt, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!itemName.Succeeded)
            {
                return NewExecutionResult.Reject("invalidLegacyInput", [.. itemName.Diagnostics]) with
                {
                    SelectedTemplate = selected.Entry,
                    ArgumentResults = [final],
                    ItemName = itemName,
                };
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var invocationRequest = new TemplateInvocationRequest(
            request.WorkingDirectory, final.ValuesByCanonicalName,
            itemName?.Name ?? request.Name ?? (provider is null ? null : selected.Entry.DefaultName), request.DryRun, request.Force);
        if (provider is not null)
        {
            NativeTemplateInvocationPreparation prepared = await provider.PrepareAsync(
                new(templater, selected, final, request, invocationRequest), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            string? diagnostic = prepared.Request is null ? null : NativeTemplateWorkflowRegistry.InvocationDiagnostic(invocationRequest, prepared.Request, final);
            if (prepared.Request is null || prepared.Diagnostics.Count > 0 || diagnostic is not null)
            {
                return NewExecutionResult.Reject("workflowPreparationFailed",
                    [.. prepared.Diagnostics, .. diagnostic is null ? [] : new[] { diagnostic }]) with
                {
                    SelectedTemplate = selected.Entry,
                    ArgumentResults = [final],
                    ItemName = itemName,
                };
            }

            invocationRequest = prepared.Request;
            final = final with
            {
                ValuesByCanonicalName = new ReadOnlyDictionary<string, string?>(
                    new Dictionary<string, string?>(invocationRequest.Parameters, StringComparer.Ordinal)),
            };
        }

        TemplateInvocationResult? effectInvocation = selectedEffects is not null && effectBinding is not null
            ? await selectedEffects.InvokeAsync(templater, selected, effectBinding, invocationRequest, cancellationToken) : null;
        cancellationToken.ThrowIfCancellationRequested();
        TemplateInvocationResult invocation = effectInvocation ?? await selected.InvokeAsync(invocationRequest, cancellationToken);
        bool success = invocation.Status == TemplateInvocationStatus.Success;

        NewExecutionResult result = new(success ? request.DryRun ? "previewed" : "created" : "invocationFailed", success)
        {
            SelectedTemplate = selected.Entry,
            ArgumentResults = [final],
            Invocation = invocation,
            ItemName = itemName,
            Diagnostics = invocation.Diagnostic is null ? [] : [invocation.Diagnostic],
        };
        if (request.DryRun || !result.HasCompletedCreation)
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

    private static string GroupIdentity(TemplateCatalogEntry entry) => entry.GroupIdentity ?? entry.Identity;
}
