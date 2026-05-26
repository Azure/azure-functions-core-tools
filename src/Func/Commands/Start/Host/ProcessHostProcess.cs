// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.Commands.Start.Host;

internal sealed class ProcessHostProcess(Process process) : IHostProcess
{
    private readonly Process _process = process ?? throw new ArgumentNullException(nameof(process));
    private readonly object _jobLock = new();
    private WindowsProcessJob? _job;

    public TextReader StandardOutput => _process.StandardOutput;

    public TextReader StandardError => _process.StandardError;

    public TextWriter StandardInput => _process.StandardInput;

    public bool HasExited => _process.HasExited;

    public int ExitCode => _process.ExitCode;

    public void Start()
    {
        if (!_process.Start())
        {
            throw new InvalidOperationException("The host process did not start.");
        }

        _job = WindowsProcessJob.TryAssign(_process);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken)
        => _process.WaitForExitAsync(cancellationToken);

    public void KillTree()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (_process.HasExited)
        {
        }
        finally
        {
            DisposeJob();
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeJob();
        _process.Dispose();
        return ValueTask.CompletedTask;
    }

    private void DisposeJob()
    {
        WindowsProcessJob? job;
        lock (_jobLock)
        {
            job = _job;
            _job = null;
        }

        job?.Dispose();
    }
}
