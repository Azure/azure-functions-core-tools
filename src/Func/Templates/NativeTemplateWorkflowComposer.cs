// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Templates.Engine;

namespace Azure.Functions.Cli.Templates;

/// <summary>
/// Captures explicit boot availability for an invocation and delegates to the CLI-owned native workflows.
/// </summary>
internal sealed class NativeTemplateWorkflowComposer(INativeTemplateAvailability availability,
    INewExecutionRunner items, IInitExecutionRunner projects, NewExecutionRenderer itemRenderer, InitExecutionRenderer projectRenderer)
{
    private readonly INativeTemplateAvailability _availability = availability ?? throw new ArgumentNullException(nameof(availability));
    private readonly INewExecutionRunner _items = items ?? throw new ArgumentNullException(nameof(items));
    private readonly IInitExecutionRunner _projects = projects ?? throw new ArgumentNullException(nameof(projects));
    private readonly NewExecutionRenderer _itemRenderer = itemRenderer ?? throw new ArgumentNullException(nameof(itemRenderer));
    private readonly InitExecutionRenderer _projectRenderer = projectRenderer ?? throw new ArgumentNullException(nameof(projectRenderer));

    public async Task<NewExecutionResult> ExecuteItemAsync(NewExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        TemplateConstraintEvaluationSnapshot availability = await _availability.CaptureAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _items.ExecuteAsync(request, availability, cancellationToken);
        }
        catch (NativeTemplateCohortException exception)
        {
            return NewExecutionResult.Reject("nativeCohortUnavailable", exception.Message);
        }
        catch (TemplateStoreRecoveryRequiredException exception)
        {
            return NewExecutionResult.Reject("templateStoreRecoveryRequired", exception.Message);
        }
    }

    public async Task<InitExecutionResult> ExecuteProjectAsync(InitExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        TemplateConstraintEvaluationSnapshot availability = await _availability.CaptureAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await _projects.ExecuteAsync(request, availability, cancellationToken);
        }
        catch (NativeTemplateCohortException exception)
        {
            return InitExecutionResult.Reject("nativeCohortUnavailable", exception.Message);
        }
        catch (TemplateStoreRecoveryRequiredException exception)
        {
            return InitExecutionResult.Reject("templateStoreRecoveryRequired", exception.Message);
        }
    }

    public async Task<NativeTemplateCommandAdapter> CreateCommandAdapterAsync(CancellationToken cancellationToken)
    {
        TemplateConstraintEvaluationSnapshot availability = await _availability.CaptureAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(_projects, _items, _projectRenderer, _itemRenderer, availability);
    }
}
