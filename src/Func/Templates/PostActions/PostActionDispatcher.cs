// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Runs a template's post actions through the <see cref="ICorePostActionProcessor"/> components registered with the
/// template engine, falling back to the template's manual instructions when an action can't be processed.
/// </summary>
internal sealed class PostActionDispatcher(IInteractionService interaction) : IIdentifiedComponent
{
    internal static readonly Guid ComponentId = new("AFAE9EA2-0A16-4939-AF7B-993054C63477");

    internal static readonly Guid RunScriptActionId = ProcessStartPostActionProcessor.ActionProcessorId;

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <inheritdoc />
    public Guid Id => ComponentId;

    /// <summary>
    /// Processes the post actions of <paramref name="creationResult"/> in declared order, prompting before 'Run script'
    /// actions on interactive consoles and declining them otherwise.
    /// </summary>
    /// <param name="templater">The templater that instantiated the template.</param>
    /// <param name="template">The exact template that produced the creation result.</param>
    /// <param name="creationResult">The result of the template instantiation or dry run.</param>
    /// <param name="isDryRun"><c>true</c> to only list the actions that would run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The combined status.</returns>
    public Task<PostActionExecutionStatus> ProcessAsync(
        Templater templater,
        ITemplateInfo template,
        ITemplateCreationResult creationResult,
        bool isDryRun,
        CancellationToken cancellationToken = default)
        => ProcessCoreAsync(templater, template, creationResult, isDryRun, allowRunScripts: null, cancellationToken);

    /// <summary>
    /// Processes the post actions of <paramref name="creationResult"/> in declared order.
    /// </summary>
    /// <param name="templater">The templater that instantiated the template.</param>
    /// <param name="template">The exact template that produced the creation result.</param>
    /// <param name="creationResult">The result of the template instantiation or dry run.</param>
    /// <param name="isDryRun"><c>true</c> to only list the actions that would run.</param>
    /// <param name="allowRunScripts">Whether 'Run script' actions may execute.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The combined status. A failed or declined action with <see cref="IPostAction.ContinueOnError"/> doesn't stop
    /// processing of later actions, but still contributes its status. A declined 'Run script' action contributes
    /// <see cref="PostActionExecutionStatus.Cancelled"/>.
    /// </returns>
    public Task<PostActionExecutionStatus> ProcessAsync(
        Templater templater,
        ITemplateInfo template,
        ITemplateCreationResult creationResult,
        bool isDryRun,
        AllowRunScripts allowRunScripts,
        CancellationToken cancellationToken = default)
        => ProcessCoreAsync(templater, template, creationResult, isDryRun, allowRunScripts, cancellationToken);

    private async Task<PostActionExecutionStatus> ProcessCoreAsync(
        Templater templater,
        ITemplateInfo template,
        ITemplateCreationResult creationResult,
        bool isDryRun,
        AllowRunScripts? allowRunScripts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(templater);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(creationResult);
        ICreationEffects creationEffects = creationResult.CreationEffects
            ?? throw new ArgumentException("Creation effects are required.", nameof(creationResult));
        ICreationResult? result = creationResult.CreationResult;
        if (!isDryRun && result is null)
        {
            throw new ArgumentException("A creation result is required unless processing a dry run.", nameof(creationResult));
        }

        if (string.IsNullOrWhiteSpace(creationResult.OutputBaseDirectory))
        {
            throw new ArgumentException("An output base directory is required.", nameof(creationResult));
        }

        IReadOnlyList<IPostAction> postActions = isDryRun
            ? creationEffects.CreationResult.PostActions
            : result!.PostActions;

        if (postActions.Count == 0)
        {
            return PostActionExecutionStatus.Success;
        }

        _interaction.WriteBlankLine();
        _interaction.WriteLine("Processing post-creation actions...");

        IEngineEnvironmentSettings environment = templater.Settings;
        PostActionExecutionStatus status = PostActionExecutionStatus.Success;
        foreach (IPostAction action in postActions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isDryRun)
            {
                _interaction.WriteLine("Action would have been taken automatically:");
                WriteDescription(action);
                continue;
            }

            PostActionExecutionStatus actionStatus = PostActionExecutionStatus.Success;
            if (!environment.Components.TryGetComponent(action.ActionId, out ICorePostActionProcessor? processor) || processor is null)
            {
                _interaction.WriteError($"Post action '{action.ActionId}' is not supported.");
                WriteDescription(action);
                WriteManualInstructions(action);
                actionStatus = PostActionExecutionStatus.Failure;
            }
            else if (action.ActionId == RunScriptActionId
                && !await AuthorizeRunScriptAsync(action, allowRunScripts, cancellationToken))
            {
                actionStatus = PostActionExecutionStatus.Cancelled;
            }
            else
            {
                EnginePostActionContext context = new(environment, action, creationEffects, result!, creationResult.OutputBaseDirectory, template);
                if (!await processor.ProcessAsync(context, cancellationToken))
                {
                    _interaction.WriteError("Post action failed.");
                    WriteDescription(action);
                    WriteManualInstructions(action);
                    actionStatus = PostActionExecutionStatus.Failure;
                }
            }

            status |= actionStatus;
            if (actionStatus != PostActionExecutionStatus.Success && !action.ContinueOnError)
            {
                break;
            }

            _interaction.WriteBlankLine();
        }

        return status;
    }

    /// <summary>
    /// Applies <paramref name="allowRunScripts"/> to a 'Run script' action, reporting a declined action with the
    /// command and manual instructions.
    /// </summary>
    private async Task<bool> AuthorizeRunScriptAsync(IPostAction action, AllowRunScripts? allowRunScripts, CancellationToken cancellationToken)
    {
        switch (allowRunScripts ?? (_interaction.IsInteractive ? AllowRunScripts.Prompt : AllowRunScripts.No))
        {
            case AllowRunScripts.Yes:
                return true;
            case AllowRunScripts.Prompt:
                if (!_interaction.IsInteractive)
                {
                    _interaction.WriteWarning("Execution of 'Run script' post action was declined because confirmation is unavailable.");
                    WriteDescription(action);
                    break;
                }

                _interaction.WriteLine("Template is configured to run the following action:");
                WriteDescription(action);
                WriteCommand(action);
                if (await _interaction.ConfirmAsync(
                    "Do you want to run this action?", defaultValue: false, whenInputUnavailable: false, cancellationToken))
                {
                    return true;
                }

                _interaction.WriteWarning("Execution of 'Run script' post action was declined.");
                break;
            default:
                _interaction.WriteWarning("Execution of 'Run script' post action is not allowed.");
                WriteDescription(action);
                break;
        }

        if (!string.IsNullOrWhiteSpace(action.ManualInstructions))
        {
            _interaction.WriteLine($"Manual instructions: {action.ManualInstructions}");
        }

        WriteCommand(action);
        return false;
    }

    private void WriteDescription(IPostAction action)
    {
        if (!string.IsNullOrWhiteSpace(action.Description))
        {
            _interaction.WriteLine($"  {action.Description}");
        }
    }

    private void WriteManualInstructions(IPostAction action)
    {
        if (string.IsNullOrWhiteSpace(action.ManualInstructions))
        {
            return;
        }

        _interaction.WriteLine($"Manual instructions: {action.ManualInstructions}");
        WriteCommand(action);
    }

    private void WriteCommand(IPostAction action)
    {
        if (action.ActionId == RunScriptActionId
            && action.Args is not null
            && action.Args.TryGetValue("executable", out string? executable))
        {
            action.Args.TryGetValue("args", out string? args);
            _interaction.WriteLine(line => line.Plain("Actual command: ").Command($"{executable} {args}".TrimEnd()));
        }
    }
}
