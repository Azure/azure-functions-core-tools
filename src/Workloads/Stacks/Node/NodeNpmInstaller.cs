// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;

namespace Azure.Functions.Cli.Workloads.Node;

/// <summary>
/// Runs the Node initializer's fixed npm dependency installation.
/// </summary>
internal class NodeNpmInstaller
{
    /// <summary>
    /// Preserves the legacy initializer's best-effort failure result.
    /// </summary>
    public virtual async Task<(int ExitCode, string Stderr)> InstallAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            return await InstallRequiredAsync(workingDirectory, cancellationToken);
        }
        catch (Exception ex)
        {
            // Preserve the legacy initializer's best-effort result contract.
            return (-1, ex.Message);
        }
    }

    /// <summary>
    /// Runs the fixed installation while preserving cancellation for native preparation.
    /// </summary>
    public virtual async Task<(int ExitCode, string Stderr)> InstallRequiredAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        NodeNpmProcess started;
        try
        {
            started = StartProcess(workingDirectory);
        }
        catch (Win32Exception exception)
        {
            return (-1, exception.Message);
        }
        using NodeNpmProcess process = started;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            return (process.ExitCode, await stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (InvalidOperationException)
            {
                // The owned child can exit between the state check and termination.
            }
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
    }

    internal virtual NodeNpmProcess StartProcess(string workingDirectory)
    {
        Process process = Process.Start(CreateStartInfo(workingDirectory, OperatingSystem.IsWindows()))
            ?? throw new Win32Exception("Failed to start 'npm' process.");
        return new ChildProcess(process);
    }

    internal static ProcessStartInfo CreateStartInfo(string workingDirectory, bool isWindows)
    {
        ProcessStartInfo info = new()
        {
            FileName = isWindows ? "cmd.exe" : "npm",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (isWindows)
        {
            // The command is fixed workload code, never a template-supplied shell expression.
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("npm");
            info.ArgumentList.Add("install");
        }
        else
        {
            info.ArgumentList.Add("install");
        }
        return info;
    }

    private sealed class ChildProcess(Process process) : NodeNpmProcess
    {
        private readonly Process _process = process ?? throw new ArgumentNullException(nameof(process));
        public override TextReader StandardOutput => _process.StandardOutput;
        public override TextReader StandardError => _process.StandardError;
        public override bool HasExited => _process.HasExited;
        public override int ExitCode => _process.ExitCode;
        public override Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);
        public override void Kill() => _process.Kill(entireProcessTree: true);
        public override void Dispose() => _process.Dispose();
    }
}

internal abstract class NodeNpmProcess : IDisposable
{
    public abstract TextReader StandardOutput { get; }
    public abstract TextReader StandardError { get; }
    public abstract bool HasExited { get; }
    public abstract int ExitCode { get; }
    public abstract Task WaitForExitAsync(CancellationToken cancellationToken);
    public abstract void Kill();
    public abstract void Dispose();
}
