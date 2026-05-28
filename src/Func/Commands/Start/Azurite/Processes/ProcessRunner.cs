// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Azure.Functions.Cli.Commands.Start.Azurite.Processes;

/// <inheritdoc cref="IProcessRunner" />
internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProcessStartInfo startInfo = new()
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string arg in request.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using Process process = new() { StartInfo = startInfo };

        StringBuilder stdout = new();
        StringBuilder stderr = new();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessOutcome(
                    ExitCode: null,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    TimedOut: false,
                    ExecutableNotFound: true);
            }
        }
        catch (Win32Exception)
        {
            return new ProcessOutcome(
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                TimedOut: false,
                ExecutableNotFound: true);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenSource timeoutCts = new(request.Timeout);
        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        bool timedOut = false;
        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }

        // Drain any pending async output before reading the buffers.
        process.WaitForExit();

        return new ProcessOutcome(
            ExitCode: timedOut ? null : process.ExitCode,
            StandardOutput: stdout.ToString(),
            StandardError: stderr.ToString(),
            TimedOut: timedOut,
            ExecutableNotFound: false);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill.
        }
        catch (NotSupportedException)
        {
            // Platform without process-tree kill support; nothing else we can do.
        }
    }
}
