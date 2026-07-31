// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.TemplateEngine.Abstractions;

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Aggregated outcome of running every post action for one instantiation.
/// </summary>
/// <param name="Succeeded">False only when a non-continuable post action failed.</param>
/// <param name="ModifiedFiles">Project files any handler changed in place.</param>
/// <param name="Messages">Follow-up instructions and non-fatal warnings to show the user.</param>
/// <param name="FailureMessage">The hard-failure message when <paramref name="Succeeded"/> is false.</param>
/// <param name="FailureException">The hard-failure exception, if any.</param>
/// <param name="PreserveStagedContent">
/// When true the scaffolder must keep the append-flow staging directory so the
/// recovery path named in <paramref name="FailureMessage"/> survives.
/// </param>
internal sealed record FuncPostActionDispatchResult(
    bool Succeeded,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> Messages,
    string? FailureMessage = null,
    Exception? FailureException = null,
    bool PreserveStagedContent = false);

/// <summary>
/// Runs the post actions an instantiated template declared by dispatching each
/// on its <c>ActionId</c> to an allowlisted handler. There is deliberately no
/// generic "run script" path: an unrecognised action falls through to its
/// declared manual instructions so template content can never execute code.
/// </summary>
internal interface IFuncPostActionDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="postActions"/> in order, aggregating modified
    /// files and messages and stopping at the first non-continuable failure.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="postActions"/> is null.</exception>
    public Task<FuncPostActionDispatchResult> DispatchAsync(
        IReadOnlyList<IPostAction> postActions,
        string outputBasePath,
        IReadOnlyList<string> createdFiles,
        string projectDirectory,
        string functionName,
        IReadOnlyDictionary<string, string?> parameterValues,
        CancellationToken cancellationToken);
}
