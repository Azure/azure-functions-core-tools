// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Text;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Executes <c>dotnet</c> CLI commands in a child process.
/// </summary>
internal sealed class DotnetCliRunner(IDotnetPathResolver pathResolver) : IDotnetCliRunner
{
    private readonly IDotnetPathResolver _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));

    public async Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        // Output is captured so it can be attached to the exception on failure, but is otherwise discarded.
        StringBuilder stdout = new();
        StringBuilder stderr = new();

        // Each callback is invoked only from its own stream's thread (serialized per stream), so the two
        // builders are never touched concurrently and need no synchronization.
        int exitCode = await ExecuteAsync(
            arguments,
            workingDirectory,
            line => stdout.AppendLine(line),
            line => stderr.AppendLine(line),
            cancellationToken);

        if (exitCode != 0)
        {
            throw new DotnetCliException(exitCode, stderr.ToString(), stdout.ToString(), string.Join(' ', arguments));
        }
    }

    public async Task RunStreamingAsync(IReadOnlyList<string> arguments, string? workingDirectory, Action<string>? onOutputLine,
        Action<string>? onErrorLine, CancellationToken cancellationToken)
    {
        int exitCode = await ExecuteAsync(arguments, workingDirectory, onOutputLine, onErrorLine, cancellationToken);

        if (exitCode != 0)
        {
            // Output has already been surfaced live through the callbacks, so the exception only needs
            // to carry the exit code and command.
            throw new DotnetCliException(exitCode, string.Empty, string.Empty, string.Join(' ', arguments));
        }
    }

    /// <summary>
    /// Starts <c>dotnet</c>, streams each line of standard output and standard error to the supplied
    /// callbacks as it is produced, waits for exit, and returns the process exit code.
    /// </summary>
    private async Task<int> ExecuteAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string>? onOutputLine,
        Action<string>? onErrorLine,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        using Process process = new() { StartInfo = CreateStartInfo(arguments, workingDirectory) };

        // stdout and stderr are delivered on separate thread-pool threads, so each stream gets its own
        // completion signal. The handlers share no mutable state, so the two threads never race each other;
        // each line callback is invoked only from its own stream's thread (serialized per stream).
        TaskCompletionSource stdoutComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource stderrComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutComplete.TrySetResult();
            }
            else
            {
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrComplete.TrySetResult();
            }
            else
            {
                onErrorLine?.Invoke(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start dotnet process.");
        }

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            // WaitForExitAsync does not guarantee the async stream readers have drained, so wait for the
            // trailing (null Data) sentinel on both streams to ensure every line has been delivered.
            await stdoutComplete.Task;
            await stderrComplete.Task;

            return process.ExitCode;
        }
        finally
        {
            KillProcess(process);
        }
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments, string? workingDirectory)
    {
        ProcessStartInfo psi = new()
        {
            FileName = _pathResolver.Resolve(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (workingDirectory is not null)
        {
            psi.WorkingDirectory = workingDirectory;
        }

        foreach (string arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

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
            // This is best effort and we don't want to mask the original exception if the process has already exited.
        }
    }
}
