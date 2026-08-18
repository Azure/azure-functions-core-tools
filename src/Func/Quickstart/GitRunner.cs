// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Executes <c>git</c> CLI commands in a child process with environment
/// variables that suppress credential prompts and interactive behaviour.
/// </summary>
internal sealed class GitRunner(IProcessRunner processRunner) : IGitRunner
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _versionProbeTimeout = TimeSpan.FromSeconds(5);

    // Per-process env vars that suppress credential prompts and interactive behaviour.
    private static readonly Dictionary<string, string> _gitEnvironment = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_ASKPASS"] = "echo",
        ["GIT_SSH_COMMAND"] = "echo",
    };

    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        await RunCoreAsync(arguments, workingDirectory, cancellationToken);
    }

    public async Task<string> RunWithOutputAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        return await RunCoreAsync(arguments, workingDirectory, cancellationToken);
    }

    private async Task<string> RunCoreAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessRunRequest request = new("git", arguments, workingDirectory, _processTimeout, _gitEnvironment);
        ProcessOutcome outcome = await _processRunner.RunAsync(request, cancellationToken);

        if (outcome.ExecutableNotFound)
        {
            throw new InvalidOperationException("Failed to start git process. Is git installed and on PATH?");
        }

        if (outcome.TimedOut)
        {
            throw new GitRunnerException(
                -1,
                "Process timed out.",
                outcome.StandardOutput,
                string.Join(' ', arguments));
        }

        if (outcome.ExitCode != 0)
        {
            throw new GitRunnerException(
                outcome.ExitCode!.Value,
                outcome.StandardError,
                outcome.StandardOutput,
                string.Join(' ', arguments));
        }

        return outcome.StandardOutput.Trim();
    }

    public async Task<string?> TryGetVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessRunRequest request = new("git", ["--version"], WorkingDirectory: null, _versionProbeTimeout, _gitEnvironment);
            ProcessOutcome outcome = await _processRunner.RunAsync(request, cancellationToken);

            if (outcome.ExecutableNotFound || outcome.TimedOut || outcome.ExitCode != 0)
            {
                return null;
            }

            return outcome.StandardOutput.Trim();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
