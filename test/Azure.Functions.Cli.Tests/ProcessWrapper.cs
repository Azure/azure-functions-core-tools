using System;
using System.Diagnostics;

namespace Azure.Functions.Cli.Tests;

internal static class ProcessWrapper
{
    public static void RunProcess(string fileName, string arguments, string workingDirectory, Action<string> writeOutput = null, Action<string> writeError = null)
    {
        TimeSpan procTimeout = TimeSpan.FromMinutes(3);

        ProcessStartInfo startInfo = new()
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        if (!string.IsNullOrEmpty(arguments))
        {
            startInfo.Arguments = arguments;
        }

        Process testProcess = Process.Start(startInfo);

        bool completed = testProcess.WaitForExit((int)procTimeout.TotalMilliseconds);

        if (!completed)
        {
            testProcess.Kill();
            throw new TimeoutException($"Process '{fileName} {arguments}' in working directory '{workingDirectory}' did not complete in {procTimeout}.");
        }

        var standardOut = testProcess.StandardOutput.ReadToEnd();
        var standardError = testProcess.StandardError.ReadToEnd();

        if (testProcess.ExitCode != 0)
        {
            throw new InvalidOperationException($"Process '{fileName} {arguments}' in working directory '{workingDirectory}' exited with code '{testProcess.ExitCode}'.{Environment.NewLine}" +
                $"Output:{Environment.NewLine}{standardOut}{Environment.NewLine}Error:{Environment.NewLine}{standardError}");
        }

        writeOutput?.Invoke(standardOut);
        writeError?.Invoke(standardError);
    }
}