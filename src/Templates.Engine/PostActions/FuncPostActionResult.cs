// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Templates.Engine;

/// <summary>
/// Outcome of dispatching a single post action. Handlers return a typed result
/// rather than throwing for expected outcomes; the dispatcher aggregates them.
/// </summary>
internal abstract record FuncPostActionResult
{
    private FuncPostActionResult()
    {
    }

    /// <summary>
    /// The post action ran to completion. <see cref="ModifiedFiles"/> lists any
    /// project files it changed in place; <see cref="Instructions"/> carries
    /// follow-up guidance to show the user (e.g. blueprint registration steps).
    /// </summary>
    internal sealed record Succeeded : FuncPostActionResult
    {
        /// <summary>
        /// Project files the action changed in place. Empty when it created no
        /// changes to an existing file.
        /// </summary>
        internal IReadOnlyList<string> ModifiedFiles { get; init; } = [];

        /// <summary>
        /// Follow-up instructions to show the user. Empty when none.
        /// </summary>
        internal IReadOnlyList<string> Instructions { get; init; } = [];
    }

    /// <summary>
    /// The post action could not be executed automatically; the user should be
    /// shown <paramref name="ManualInstructions"/> to complete it by hand.
    /// </summary>
    /// <param name="ManualInstructions">Human-readable follow-up steps.</param>
    internal sealed record ManualInstructionsRequired(string ManualInstructions) : FuncPostActionResult;

    /// <summary>
    /// The post action failed. When <paramref name="ContinueOnError"/> is
    /// <c>true</c> the engine marked the action non-fatal and scaffolding may
    /// still be reported as successful.
    /// </summary>
    /// <param name="Message">Description of the failure.</param>
    /// <param name="ContinueOnError">Whether the failure is non-fatal.</param>
    /// <param name="Exception">The underlying exception, if any.</param>
    /// <param name="PreserveStagedContent">
    /// When true the caller must keep the provider-owned staging directory so a
    /// recovery path named in <paramref name="Message"/> survives; the default
    /// lets the caller clean staging up.
    /// </param>
    internal sealed record Failed(
        string Message, bool ContinueOnError, Exception? Exception = null, bool PreserveStagedContent = false) : FuncPostActionResult;
}
