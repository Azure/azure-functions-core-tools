// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;

namespace Azure.Functions.Cli.Quickstart;

/// <summary>
/// Executes <c>git</c> CLI commands in a child process with environment
/// variables that suppress credential prompts and interactive behaviour.
/// </summary>
internal sealed class GitRunner : IGitRunner
{
    private static readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(60);

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

        ProcessStartInfo psi = CreateStartInfo(workingDirectory);
        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_processTimeout);
        CancellationToken linked = timeoutCts.Token;

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process. Is git installed and on PATH?");

        try
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(linked);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(linked);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(linked);

            if (process.ExitCode != 0)
            {
                throw new GitRunnerException(
                    process.ExitCode,
                    stderrTask.Result,
                    stdoutTask.Result,
                    string.Join(' ', arguments));
            }

            return stdoutTask.Result.Trim();
        }
        finally
        {
            KillProcess(process);
        }
    }

    private static readonly TimeSpan _versionProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<string?> TryGetVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            ProcessStartInfo psi = CreateStartInfo(workingDirectory: null);
            psi.ArgumentList.Add("--version");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_versionProbeTimeout);
            CancellationToken linked = timeoutCts.Token;

            using Process process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start git process.");

            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(linked);
                await process.WaitForExitAsync(linked);

                return process.ExitCode == 0 ? output.Trim() : null;
            }
            finally
            {
                KillProcess(process);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            // git not installed, not on PATH, or timed out — fall back to HTTP
            return null;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        // Per-process env vars — do NOT affect the user's shell or global git config.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GIT_ASKPASS"] = "echo";
        psi.Environment["GIT_SSH_COMMAND"] = "echo";

        return psi;
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Best effort — don't mask the original exception if the process already exited.
        }
    }
}
