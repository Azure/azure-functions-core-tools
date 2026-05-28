// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <summary>
/// Launches short-lived child processes for the CLI. Wraps
/// <see cref="System.Diagnostics.Process"/> behind a substitutable seam so the
/// rest of the Azurite discovery code can be unit-tested without spawning real
/// processes.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Runs the requested process to completion (or to the configured timeout).
    /// </summary>
    /// <param name="request">The process invocation to perform.</param>
    /// <param name="cancellationToken">Caller cancellation token. When triggered, the process is killed and the returned outcome reports <see cref="ProcessOutcome.TimedOut"/> as <c>false</c>; cancellation surfaces as <see cref="OperationCanceledException"/>.</param>
    /// <returns>The captured <see cref="ProcessOutcome"/>. Never <c>null</c>.</returns>
    public Task<ProcessOutcome> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);
}
