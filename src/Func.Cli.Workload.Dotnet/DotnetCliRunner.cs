// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Common;

namespace Azure.Functions.Cli.Workload.Dotnet;

/// <summary>
/// Runs the dotnet CLI by spawning a child process.
/// </summary>
public class DotnetCliRunner : IDotnetCliRunner
{
    public async Task<DotnetCliResult> RunAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new GracefulException("Failed to start 'dotnet' process.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new GracefulException(
                "The 'dotnet' CLI was not found on your PATH.",
                "Install the .NET SDK from https://dot.net/download");
        }

        using (process)
        {
            await using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* process may have already exited */ }
            });

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new DotnetCliResult(process.ExitCode, stdout, stderr);
        }
    }
}
