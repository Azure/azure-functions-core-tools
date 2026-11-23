// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Azure.Functions.Cli.Workloads.Python;

/// <summary>
/// Process helpers shared by the venv/pip and uv preparation paths.
/// </summary>
internal static class PythonProcessRunner
{
    /// <summary>
    /// Streams each line of stdout and stderr to the callbacks as the process produces them, waits for exit,
    /// and returns the exit code plus whether the process actually launched. A failure to launch (executable
    /// not on PATH) is reported via Launched=false so the caller can fall back to another executable.
    /// </summary>
    public static async Task<(int ExitCode, bool Launched)> TryRunStreamingAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        Action<string> onOutputLine,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };

        // stdout and stderr are delivered on separate thread-pool threads, so each stream gets its own
        // completion signal; each line callback is invoked only from its own stream's thread.
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
                onOutputLine(e.Data);
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
                onErrorLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                onErrorLine($"Failed to start '{fileName}' process.");
                return (-1, true);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // WaitForExitAsync does not guarantee the async stream readers have drained, so wait for the
            // trailing (null Data) sentinel on both streams to ensure every line has been delivered.
            await stdoutComplete.Task.ConfigureAwait(false);
            await stderrComplete.Task.ConfigureAwait(false);

            return (process.ExitCode, true);
        }
        catch (Win32Exception)
        {
            // Executable wasn't on PATH; let the caller try a fallback.
            return (-1, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onErrorLine(ex.Message);
            return (-1, true);
        }
        finally
        {
            KillProcess(process);
        }
    }

    /// <summary>
    /// Runs a process, captures stdout verbatim (newline-joined), and streams stderr lines to
    /// <paramref name="onErrorLine"/>. Returns -1 when the process cannot be launched.
    /// </summary>
    public static async Task<(int ExitCode, string Output)> RunCapturingAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        Action<string> onErrorLine,
        CancellationToken cancellationToken)
    {
        StringBuilder output = new();
        object gate = new();

        (int exitCode, bool launched) = await TryRunStreamingAsync(
            fileName,
            args,
            workingDirectory,
            line =>
            {
                lock (gate)
                {
                    output.Append(line).Append('\n');
                }
            },
            onErrorLine,
            cancellationToken).ConfigureAwait(false);

        if (!launched)
        {
            onErrorLine($"Failed to start '{fileName}'.");
            return (-1, string.Empty);
        }

        lock (gate)
        {
            return (exitCode, output.ToString());
        }
    }

    internal static void KillProcess(Process process)
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
            // Best effort: don't mask the original outcome if the process has already exited.
        }
    }
}
