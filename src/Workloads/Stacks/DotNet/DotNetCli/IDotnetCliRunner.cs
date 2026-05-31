// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Runs <c>dotnet</c> CLI commands in a child process.
/// </summary>
internal interface IDotnetCliRunner
{
    /// <summary>
    /// Executes <c>dotnet</c> with the supplied arguments.
    /// </summary>
    /// <param name="arguments">The arguments to pass to <c>dotnet</c>.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    /// <exception cref="DotnetCliException">The process exited with a non-zero exit code.</exception>
    public Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Executes <c>dotnet</c> with the supplied arguments, invoking the supplied callbacks for each
    /// line of standard output and standard error as it is produced by the child process.
    /// </summary>
    /// <param name="arguments">The arguments to pass to <c>dotnet</c>.</param>
    /// <param name="workingDirectory">Optional working directory for the process.</param>
    /// <param name="onOutputLine">Invoked for each line written to standard output. Invocations for
    /// standard output are serialized with respect to one another but may run concurrently with
    /// <paramref name="onErrorLine"/>; the callback must be safe to call from a thread-pool thread.</param>
    /// <param name="onErrorLine">Invoked for each line written to standard error. Invocations for
    /// standard error are serialized with respect to one another but may run concurrently with
    /// <paramref name="onOutputLine"/>; the callback must be safe to call from a thread-pool thread.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    /// <exception cref="DotnetCliException">The process exited with a non-zero exit code.</exception>
    public Task RunStreamingAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string>? onOutputLine,
        Action<string>? onErrorLine,
        CancellationToken cancellationToken);
}
