// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Runs <c>git</c> CLI commands in a child process with environment
/// variables that suppress credential prompts and interactive behaviour.
/// </summary>
internal interface IGitRunner
{
    /// <summary>
    /// Executes <c>git</c> with the supplied arguments.
    /// </summary>
    /// <param name="arguments">The arguments to pass to <c>git</c>.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The process could not be started (git not installed).</exception>
    /// <exception cref="GitRunnerException">The process exited with a non-zero exit code.</exception>
    public Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Executes <c>git</c> and returns the trimmed standard output.
    /// </summary>
    /// <param name="arguments">The arguments to pass to <c>git</c>.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The process could not be started (git not installed).</exception>
    /// <exception cref="GitRunnerException">The process exited with a non-zero exit code.</exception>
    public Task<string> RunWithOutputAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Probes for git availability by running <c>git --version</c>.
    /// Returns the version string on success, or <see langword="null"/> if
    /// git is not installed or not on PATH.
    /// </summary>
    public Task<string?> TryGetVersionAsync(CancellationToken cancellationToken);
}
