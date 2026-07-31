// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Extensions.Logging;
using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Default post-action dispatcher. Keys registered
/// <see cref="IFuncPostActionHandler"/> instances by their <c>ActionId</c>;
/// unhandled actions fall through to their manual instructions.
/// </summary>
internal sealed class FuncPostActionDispatcher : IFuncPostActionDispatcher
{
    private readonly IReadOnlyDictionary<Guid, IFuncPostActionHandler> _handlers;
    private readonly ILogger<FuncPostActionDispatcher> _logger;

    public FuncPostActionDispatcher(IEnumerable<IFuncPostActionHandler> handlers, ILogger<FuncPostActionDispatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var map = new Dictionary<Guid, IFuncPostActionHandler>();
        foreach (IFuncPostActionHandler handler in handlers)
        {
            map[handler.ActionId] = handler;
        }

        _handlers = map;
    }

    /// <inheritdoc />
    public async Task<FuncPostActionDispatchResult> DispatchAsync(
        IReadOnlyList<IPostAction> postActions,
        string outputBasePath,
        IReadOnlyList<string> createdFiles,
        string projectDirectory,
        string functionName,
        IReadOnlyDictionary<string, string?> parameterValues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(postActions);

        var modifiedFiles = new List<string>();
        var messages = new List<string>();

        foreach (IPostAction postAction in postActions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_handlers.TryGetValue(postAction.ActionId, out IFuncPostActionHandler? handler))
            {
                if (!string.IsNullOrWhiteSpace(postAction.ManualInstructions))
                {
                    messages.Add(postAction.ManualInstructions);
                }

                _logger.LogDebug("No handler for post-action {ActionId}; fell through to manual instructions.", postAction.ActionId);
                continue;
            }

            var context = new FuncPostActionContext(
                postAction, outputBasePath, createdFiles, projectDirectory, functionName, parameterValues);
            FuncPostActionResult result = await handler.ExecuteAsync(context, cancellationToken);

            switch (result)
            {
                case FuncPostActionResult.Succeeded succeeded:
                    modifiedFiles.AddRange(succeeded.ModifiedFiles);
                    messages.AddRange(succeeded.Instructions);
                    break;

                case FuncPostActionResult.ManualInstructionsRequired manual:
                    messages.Add(manual.ManualInstructions);
                    break;

                case FuncPostActionResult.Failed failed when failed.ContinueOnError:
                    _logger.LogWarning("Post-action {ActionId} failed (non-fatal): {Message}", postAction.ActionId, failed.Message);
                    messages.Add($"Warning: {failed.Message}");
                    break;

                case FuncPostActionResult.Failed failed:
                    _logger.LogError(failed.Exception, "Post-action {ActionId} failed: {Message}", postAction.ActionId, failed.Message);
                    return new FuncPostActionDispatchResult(
                        false, modifiedFiles, messages, failed.Message, failed.Exception, failed.PreserveStagedContent);
            }
        }

        return new FuncPostActionDispatchResult(true, modifiedFiles, messages);
    }
}
