// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Azure.Functions.Cli.Common.Processes;

/// <inheritdoc cref="IProcessRunner" />
internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentOutOfRangeException.ThrowIfNegative(request.MaximumStandardInputBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(request.MaximumCapturedOutputCharacters);
        if (request.StandardInput is not null && Encoding.UTF8.GetByteCount(request.StandardInput) > request.MaximumStandardInputBytes)
        {
            throw new ArgumentException("The process input exceeds the configured byte limit.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new()
        {
            FileName = request.FileName,
            RedirectStandardInput = request.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (startInfo.RedirectStandardInput)
        {
            startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        }

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (string arg in request.Arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach ((string name, string? value) in request.EnvironmentVariables)
            {
                startInfo.Environment[name] = value;
            }
        }

        using Process process = new() { StartInfo = startInfo };
        using CancellationTokenSource timeoutCts = new(request.Timeout);
        using var operationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

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
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
        {
            return new ProcessOutcome(
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                TimedOut: false,
                ExecutableNotFound: true);
        }

        Task<string> stdout = CaptureAsync(process.StandardOutput, request.MaximumCapturedOutputCharacters, operationCts.Token);
        Task<string> stderr = CaptureAsync(process.StandardError, request.MaximumCapturedOutputCharacters, operationCts.Token);
        Task input = request.StandardInput is null
            ? Task.CompletedTask
            : WriteInputAsync(process.StandardInput, request.StandardInput, operationCts.Token);
        var operation = Task.WhenAll(input, stdout, stderr, process.WaitForExitAsync(operationCts.Token));
        try
        {
            // The deadline includes stdin and pipe drains, not just the lifetime of the initial process.
            await operation.WaitAsync(operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            operationCts.Cancel();
            ClosePipes(process, request.StandardInput is not null);
            await ObserveStoppedOperationAsync(operation);

            cancellationToken.ThrowIfCancellationRequested();
            return new ProcessOutcome(
                ExitCode: null,
                StandardOutput: stdout.IsCompletedSuccessfully ? await stdout : string.Empty,
                StandardError: stderr.IsCompletedSuccessfully ? await stderr : string.Empty,
                TimedOut: true,
                ExecutableNotFound: false);
        }
        catch
        {
            TryKill(process);
            operationCts.Cancel();
            ClosePipes(process, request.StandardInput is not null);
            await ObserveStoppedOperationAsync(operation);
            throw;
        }

        return new ProcessOutcome(
            ExitCode: process.ExitCode,
            StandardOutput: await stdout,
            StandardError: await stderr,
            TimedOut: false,
            ExecutableNotFound: false);
    }

    private static async Task WriteInputAsync(StreamWriter writer, string text, CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(text.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            // Disposing the pipe, rather than flushing StreamWriter on cancellation, cannot block on a full pipe.
            writer.BaseStream.Dispose();
        }
    }

    private static async Task<string> CaptureAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        StringBuilder captured = new();
        char[] buffer = new char[4096];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                int remaining = limit - captured.Length;
                if (remaining > 0)
                {
                    captured.Append(buffer, 0, Math.Min(remaining, count));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Retain already captured diagnostics after the whole-operation deadline.
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
            // Teardown closes inherited pipes to make a bounded drain possible.
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            // Teardown may close a pipe before its asynchronous read observes cancellation.
        }

        return captured.ToString();
    }

    private static void ClosePipes(Process process, bool hasInput)
    {
        if (hasInput)
        {
            process.StandardInput.BaseStream.Dispose();
        }

        process.StandardOutput.BaseStream.Dispose();
        process.StandardError.BaseStream.Dispose();
    }

    private static async Task ObserveStoppedOperationAsync(Task operation)
    {
        try
        {
            await operation.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        }
        catch (Exception)
        {
            // Preserve the initiating timeout/cancellation/I/O error while observing late pipe failures.
            _ = operation.ContinueWith(
                task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
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
        catch (Win32Exception)
        {
            // Cleanup is best effort if the owned process has exited or its handle is no longer accessible.
        }
    }
}
