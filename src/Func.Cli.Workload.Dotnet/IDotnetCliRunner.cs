// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Abstraction over the dotnet CLI for testability. Wraps process execution
/// so workload code doesn't directly spawn processes.
/// </summary>
public interface IDotnetCliRunner
{
    /// <summary>
    /// Runs a dotnet command and returns the result.
    /// </summary>
    /// <param name="arguments">Arguments to pass to the dotnet CLI.</param>
    /// <param name="workingDirectory">Working directory for the command.</param>
    /// <param name="cancellationToken">Cancellation token — kills the child process on cancel.</param>
    public Task<DotnetCliResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a dotnet CLI invocation.
/// </summary>
/// <param name="ExitCode">Process exit code (0 = success).</param>
/// <param name="StandardOutput">Captured stdout.</param>
/// <param name="StandardError">Captured stderr.</param>
public record DotnetCliResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
}
