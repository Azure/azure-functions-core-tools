// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Runs the Node initializer's fixed npm dependency installation.
/// </summary>
internal class NodeNpmInstaller
{
    public virtual async Task<(int ExitCode, string Stderr)> InstallAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            // Windows resolves npm through its command shim.
            bool isWindows = OperatingSystem.IsWindows();
            var psi = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "npm",
                Arguments = isWindows ? "/c npm install" : "install",
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, "Failed to start 'npm' process.");
            }

            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return (process.ExitCode, stderr);
        }
        catch (Exception ex)
        {
            // Preserve the legacy initializer's best-effort result contract.
            return (-1, ex.Message);
        }
    }
}
