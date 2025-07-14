// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.Cli.UnitTests.Helpers
{
    internal class ProcessHelper
    {
        private const string CommandExe = "cmd";

        public static void RunProcess(string fileName, string arguments, string workingDirectory, Action<string> writeOutput = null, Action<string> writeError = null)
        {
            TimeSpan procTimeout = TimeSpan.FromMinutes(3);

            ProcessStartInfo startInfo = new()
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                FileName = fileName
            };

            if (!string.IsNullOrEmpty(arguments))
            {
                startInfo.Arguments = arguments;
            }

            using Process testProcess = new()
            {
                StartInfo = startInfo,
            };

            testProcess.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    writeOutput?.Invoke(e.Data);
                }
            };

            testProcess.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    writeError?.Invoke(e.Data);
                }
            };

            testProcess.Start();
            testProcess.BeginOutputReadLine();
            testProcess.BeginErrorReadLine();

            bool completed = false;
            try
            {
                completed = testProcess.WaitForExit((int)procTimeout.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Process '{fileName} {arguments}' in working directory '{workingDirectory}' threw exception '{ex}'.");
            }

            if (!completed)
            {
                throw new TimeoutException($"Process '{fileName} {arguments}' in working directory '{workingDirectory}' did not complete in {procTimeout}.");
            }

            testProcess.WaitForExit();
        }
    }
}
