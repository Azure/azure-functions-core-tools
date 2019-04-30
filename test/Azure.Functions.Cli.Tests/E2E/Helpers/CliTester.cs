using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public static class CliTester
    {
        private static string _func = System.Environment.GetEnvironmentVariable("FUNC_PATH");

        private const string StartHostCommand = "start --build";

        public static Task Run(RunConfiguration runConfiguration, ITestOutputHelper output = null, string workingDir = null, bool startHost = false) => Run(new[] { runConfiguration }, output, workingDir, startHost);

        public static async Task Run(RunConfiguration[] runConfigurations, ITestOutputHelper output = null, string workingDir = null, bool startHost = false)
        {
            string workingDirectory = workingDir ?? Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            bool cleanupDirectory = string.IsNullOrEmpty(workingDir);
            if (cleanupDirectory)
            {
                Directory.CreateDirectory(workingDirectory);
            }

            string nugetConfigPath = Path.Combine(workingDirectory, "NuGet.Config");
            File.Copy(Path.Combine(Directory.GetCurrentDirectory(), "NuGet.Config"), nugetConfigPath);

            try
            {
                await InternalRun(workingDirectory, runConfigurations, output, startHost);
            }
            finally
            {
                try
                {
                    if (cleanupDirectory)
                    {
                        Directory.Delete(workingDirectory, recursive: true);
                    }
                    else
                    {
                        File.Delete(nugetConfigPath);
                    }
                }
                catch { }
            }
        }

        private static async Task InternalRun(string workingDir, RunConfiguration[] runConfigurations, ITestOutputHelper output, bool startHost)
        {
            var hostExe = new Executable(_func, StartHostCommand, workingDirectory: workingDir);

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            foreach (var runConfiguration in runConfigurations)
            {
                Task hostTask = startHost ? hostExe.RunAsync(logStd, logErr) : Task.Delay(runConfiguration.CommandTimeout);
                stdout.Clear();
                stderr.Clear();
                var exitError = false;
                runConfiguration.PreTest?.Invoke(workingDir);

                for (var i = 0; i < runConfiguration.Commands.Length; i++)
                {
                    var command = runConfiguration.Commands[i];
                    var exe = new Executable(_func, command, workingDirectory: workingDir);

                    if (startHost && i == runConfiguration.Commands.Length - 1)
                    {
                        // Give the host time to handle the first requests before executing the final command
                        logStd($"[{DateTime.Now}] Pausing to let the Functions host handle previous requests.");
                        await Task.Delay(TimeSpan.FromSeconds(20));
                        logStd($"[{DateTime.Now}] Resuming commands.");
                    }

                    logStd($"Running: > {exe.Command}");

                    if (runConfiguration.ExpectExit || (i + 1) < runConfiguration.Commands.Length)
                    {
                        var exitCode = await exe.RunAsync(logStd, logErr, timeout: runConfiguration.CommandTimeout);
                        exitError |= exitCode != 0;
                    }
                    else
                    {
                        var exitCodeTask = exe.RunAsync(logStd, logErr);

                        try
                        {
                            await runConfiguration.Test.Invoke(workingDir, exe.Process);
                        }
                        finally
                        {
                            await Task.WhenAny(exitCodeTask, Task.Delay(runConfiguration.CommandTimeout));
                            if (!exitCodeTask.IsCompleted)
                            {
                                exe.Process.Kill();
                                throw new Exception("Expected process to exit after calling Test() and within timeout, but it didn't.");
                            }
                            else
                            {
                                exitError |= exitCodeTask.Result != 0;
                            }
                        }
                    }
                }

                if (runConfiguration.ExpectExit && runConfiguration.Test != null)
                {
                    await runConfiguration.Test.Invoke(workingDir, null);
                }

                if (startHost)
                {
                    if (hostExe.Process?.HasExited == false)
                    {
                        logStd($"[{DateTime.Now}] Terminating the Functions host.");
                        hostExe.Process.Kill();
                    }
                }


                // AssertExitError(runConfiguration, exitError);
                AssertFiles(runConfiguration, workingDir);
                AssertDirectories(runConfiguration, workingDir);
                AssertOutputContent(runConfiguration, stdout);
                AssertErrorContent(runConfiguration, stderr);

            }
            void logStd(string line)
            {
                try
                {
                    stdout.AppendLine(line);
                    output?.WriteLine($"stdout: {line}");
                }
                catch { }
            }

            void logErr(string line)
            {
                try
                {
                    stderr.AppendLine(line);
                    output?.WriteLine($"stderr: {line}");
                }
                catch { }
            }
        }

        private static void AssertExitError(RunConfiguration runConfiguration, bool exitError)
        {
            if (runConfiguration.ExitInError)
            {
                exitError.Should().BeTrue(because: $"Commands {runConfiguration.CommandsStr} " +
                "expected to have an error but they didn't.");
            }
            else
            {
                exitError.Should().BeFalse(because: $"Commands {runConfiguration.CommandsStr} " +
                "expected to not fail but failed.");
            }
        }

        private static void AssertHasStandardError(RunConfiguration runConfiguration, StringBuilder stderr)
        {
            var error = stderr.ToString().Trim(' ', '\n');
            if (runConfiguration.HasStandardError ||
                runConfiguration.ErrorContains.Any() ||
                runConfiguration.ErrorDoesntContain.Any())
            {
                error.Should().NotBeNullOrEmpty(because: "The run expects stderr");
            }
            else
            {
                error.Should().BeNullOrEmpty(because: "The run doesn't expect stderr");
            }
        }

        private static void AssertFiles(RunConfiguration runConfiguration, string path)
        {
            foreach (var fileResult in runConfiguration.CheckFiles)
            {
                var filePath = Path.Combine(path, fileResult.Name);
                if (!fileResult.Exists)
                {
                    File.Exists(filePath)
                        .Should().BeFalse(because: $"File {fileResult.Name} should not exist.");
                }
                else
                {
                    File.Exists(filePath)
                        .Should().BeTrue(because: $"File {filePath} should exist");

                    var fileContent = File.ReadAllText(filePath);
                    if (!string.IsNullOrEmpty(fileResult.ContentIs))
                    {
                        fileContent
                            .Should()
                            .Be(fileResult.ContentIs, because: $"File ({fileResult.Name}) content should match ContentIs");
                    }

                    if (fileResult.ContentContains.Any())
                    {
                        fileContent
                            .Should()
                            .ContainAll(fileResult.ContentContains, because: $"File ({fileResult.Name}) content should contain all in ContentContains");
                    }

                    if (fileResult.ContentNotContains.Any())
                    {
                        fileContent
                            .Should()
                            .NotContainAny(fileResult.ContentNotContains, because: $"File ({fileResult.Name}) contains text from the noContain list");
                    }
                }
            }
        }

        private static void AssertDirectories(RunConfiguration runConfiguration, string path)
        {
            foreach (var directoryResult in runConfiguration.CheckDirectories)
            {
                var dirPath = Path.Combine(path, directoryResult.Name);
                if (!directoryResult.Exists)
                {
                    Directory.Exists(dirPath)
                        .Should().BeFalse(because: $"Directory {directoryResult.Name} should not exist.");
                }
                else
                {
                    Directory.Exists(dirPath)
                        .Should().BeTrue(because: $"Directory {dirPath} should exist");
                }
            }
        }

        private static void AssertOutputContent(RunConfiguration runConfiguration, StringBuilder stdout)
        {
            var output = stdout.ToString().Trim();
            if (runConfiguration.OutputContains.Any())
            {
                output.Should().ContainAll(runConfiguration.OutputContains);
            }

            if (runConfiguration.OutputDoesntContain.Any())
            {
                output.Should().NotContainAny(runConfiguration.OutputDoesntContain);
            }
        }

        private static void AssertErrorContent(RunConfiguration runConfiguration, StringBuilder stderr)
        {
            var error = stderr.ToString().Trim();
            if (runConfiguration.ErrorContains.Any())
            {
                error.Should().ContainAll(runConfiguration.ErrorContains);
            }

            if (runConfiguration.ErrorDoesntContain.Any())
            {
                error.Should().NotContainAny(runConfiguration.ErrorDoesntContain);
            }
        }
    }
}
