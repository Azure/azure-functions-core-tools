// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Console;
using Azure.Functions.Cli.Templates.Engine;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Runs a template's post actions through the <see cref="IPostActionProcessor"/> components registered with the
/// template engine, falling back to the template's manual instructions when an action can't be processed.
/// </summary>
internal sealed class PostActionDispatcher(IInteractionService interaction) : IIdentifiedComponent
{
    internal static readonly Guid ComponentId = new("AFAE9EA2-0A16-4939-AF7B-993054C63477");

    internal static readonly Guid RunScriptActionId = new("3A7C4B45-1F5D-4A30-959A-51B88E82B5D2");

    private readonly IInteractionService _interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));

    /// <inheritdoc />
    public Guid Id => ComponentId;

    /// <summary>
    /// Processes the post actions of <paramref name="creationResult"/> in declared order.
    /// </summary>
    /// <param name="templater">The templater that instantiated the template.</param>
    /// <param name="creationResult">The result of the template instantiation or dry run.</param>
    /// <param name="isDryRun"><c>true</c> to only list the actions that would run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The combined status. A failed action with <see cref="IPostAction.ContinueOnError"/> doesn't contribute
    /// <see cref="PostActionExecutionStatus.Failure"/>; any other failure stops processing of later actions.
    /// </returns>
    public async Task<PostActionExecutionStatus> ProcessAsync(
        Templater templater,
        ITemplateCreationResult creationResult,
        bool isDryRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templater);
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

            // TODO: special-case run-script (RunScriptActionId) once a processor exists: honor the user's
            // allow/deny/prompt choice for running scripts, reporting Cancelled when not allowed. Until then it
            // has no processor and falls through to the manual instructions below.
            PostActionExecutionStatus actionStatus = PostActionExecutionStatus.Success;
            if (!environment.Components.TryGetComponent(action.ActionId, out IPostActionProcessor? processor) || processor is null)
            {
                _interaction.WriteError($"Post action '{action.ActionId}' is not supported.");
                WriteDescription(action);
                WriteManualInstructions(action);
                actionStatus = PostActionExecutionStatus.Failure;
            }
            else
            {
                PostActionContext context = new(environment, action, creationEffects, result!, creationResult.OutputBaseDirectory);
                if (!await processor.ProcessAsync(context, cancellationToken))
                {
                    _interaction.WriteError("Post action failed.");
                    WriteManualInstructions(action);
                    actionStatus = PostActionExecutionStatus.Failure;
                }
            }

            if (action.ContinueOnError)
            {
                actionStatus &= ~PostActionExecutionStatus.Failure;
            }

            status |= actionStatus;
            if (actionStatus != PostActionExecutionStatus.Success)
            {
                break;
            }

            _interaction.WriteBlankLine();
        }

        return status;
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
        if (action.Args is not null && action.Args.TryGetValue("executable", out string? executable))
        {
            action.Args.TryGetValue("args", out string? args);
            _interaction.WriteLine(line => line.Plain("Actual command: ").Command($"{executable} {args}".TrimEnd()));
        }
    }
}
