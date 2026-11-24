// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.PostActions;

/// <summary>
/// Processes a template post action whose <see cref="IPostAction.ActionId"/> matches <see cref="IIdentifiedComponent.Id"/>.
/// </summary>
/// <remarks>
/// Register implementations with <c>AddTemplateComponent&lt;IPostActionProcessor, TImplementation&gt;()</c> so the
/// template engine can resolve them by action ID.
/// </remarks>
internal interface IPostActionProcessor : IIdentifiedComponent
{
    /// <summary>
    /// Processes the post action.
    /// </summary>
    /// <param name="context">The post action and the template creation it belongs to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the post action succeeded; <c>false</c> if it failed and manual instructions apply.</returns>
    public Task<bool> ProcessAsync(PostActionContext context, CancellationToken cancellationToken = default);
}
