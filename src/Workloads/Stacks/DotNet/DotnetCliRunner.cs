// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Workload.DotNet;

/// <summary>
/// Executes <c>dotnet</c> CLI commands in a child process.
/// </summary>
internal sealed class DotnetCliRunner : IDotnetCliRunner
{
    public async Task RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo psi = new()
        {
            FileName = "dotnet",
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

        using Process process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        try
        {
            // Read both streams concurrently to avoid deadlock when either
            // buffer fills while the other is being awaited.
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask);
            await process.WaitForExitAsync(cancellationToken);

            string stdout = stdoutTask.Result;
            string stderr = stderrTask.Result;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"'dotnet {string.Join(' ', arguments)}' failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}{stdout}");
            }
        }
        catch (OperationCanceledException)
        {
            // Ensure the child process does not outlive the caller.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}
