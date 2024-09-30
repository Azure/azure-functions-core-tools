// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;

namespace Azure.Functions.ArtifactAssembler
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                var currentWorkingDirectory = Environment.CurrentDirectory;
                var artifactAssembler = new ArtifactAssembler(currentWorkingDirectory);

                // await artifactAssembler.AssembleArtifactsAsync();
                // Create a process start info to run dotnet test
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "test ../../test/Azure.Functions.Cli.Tests/Azure.Functions.Cli.Tests.csproj -s ../../test/Azure.Functions.Cli.Tests/E2E/StartTests_requires_nested_inproc_artifacts.runsettings", // Arguments for the command
                    RedirectStandardOutput = true, // Capture the output
                    RedirectStandardError = true,  // Capture the errors
                    UseShellExecute = false, // Needed to redirect output
                    CreateNoWindow = true    // Run without a window (useful in background apps)
                };

                // Start the process
                using (Process process = Process.Start(startInfo))
                {
                    // Capture standard output
                    string output = process.StandardOutput.ReadToEnd();
                    string errors = process.StandardError.ReadToEnd();

                    // Wait for the process to exit
                    process.WaitForExit();

                    // Display the output and errors (if any)
                    Console.WriteLine("Output: ");
                    Console.WriteLine(output);

                    if (!string.IsNullOrEmpty(errors))
                    {
                        Console.WriteLine("Errors: ");
                        Console.WriteLine(errors);
                    }

                    // Check the exit code to see if tests passed or failed
                    if (process.ExitCode == 0)
                    {
                        Console.WriteLine("Tests passed.");
                    }
                    else
                    {
                        Console.WriteLine("Tests failed.");
                    }
                }


                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running {typeof(Program).FullName} Exception: {ex}");
                return 1;
            }
        }
    }
}