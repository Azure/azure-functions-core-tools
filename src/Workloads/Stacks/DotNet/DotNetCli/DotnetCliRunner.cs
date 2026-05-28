// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.ComponentModel;
using System.Diagnostics;

namespace Azure.Functions.Cli.Workloads.DotNet;

/// <summary>
/// Executes <c>dotnet</c> CLI commands in a child process.
/// </summary>
internal sealed class DotnetCliRunner(IDotnetPathResolver pathResolver) : IDotnetCliRunner
{
    private readonly IDotnetPathResolver _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));

    public async Task RunAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        await RunCoreAsync(arguments, workingDirectory, cancellationToken);
    }

    public async Task<string> RunWithOutputAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        return await RunCoreAsync(arguments, workingDirectory, cancellationToken);
    }

    private async Task<string> RunCoreAsync(IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

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

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new DotnetCliException(
                    process.ExitCode,
                    stderr,
                    stdout,
                    string.Join(' ', arguments));
            }

            return stdout;
        }
        finally
        {
            KillProcess(process);
        }
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
