// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Handles one kind of template post action, identified by <see cref="ActionId"/>.
/// The scaffolder resolves the set of registered handlers and dispatches each
/// emitted post action to the matching one; unmatched actions fall back to
/// manual instructions.
/// </summary>
internal interface IFuncPostActionHandler
{
    /// <summary>
    /// The engine post-action id this handler services.
    /// </summary>
    public Guid ActionId { get; }

    /// <summary>
    /// Executes the post action described by <paramref name="context"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public Task<FuncPostActionResult> ExecuteAsync(FuncPostActionContext context, CancellationToken cancellationToken);
}
