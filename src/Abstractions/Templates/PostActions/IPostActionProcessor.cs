// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Processes template post actions on behalf of the registering workload.
/// </summary>
public interface IPostActionProcessor : ITemplateComponent
{
    /// <summary>
    /// Determines whether this processor supports the selected template without executing the action or causing side effects.
    /// </summary>
    /// <param name="context">The selected template's stack and language tags; either value may be empty when its tag is missing.</param>
    /// <returns>True when applicable; execution requires exactly one eligible workload processor for the action ID.</returns>
    public bool CanProcess(PostActionSelectionContext context);

    /// <summary>
    /// Processes an action after template files have been created.
    /// </summary>
    /// <param name="context">An immutable snapshot of the action and template outputs.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>True on success; false to report failure and display the template's manual instructions.</returns>
    public Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken = default);
}
