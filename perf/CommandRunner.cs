using System.Diagnostics;

namespace Func.Performance;

internal class CommandRunner(TimeSpan timeout)
{
    internal virtual async Task RunAsync(ProcessStartInfo startInfo, CommandCase command, CancellationToken cancellation)
    {
        using var process = new Process { StartInfo = startInfo };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout);
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
        }
        process.StandardInput.Close();

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellation);
        try
        {
            await Task.WhenAll(output, error, process.WaitForExitAsync(deadline.Token)).WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            if (cancellation.IsCancellationRequested)
            {
                throw;
            }

            throw new TimeoutException($"func {command.Name} exceeded {timeout.TotalSeconds} seconds.");
        }

        command.Validate(process.ExitCode, await output, await error);
    }
}
