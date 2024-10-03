using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public static class CliTester
    {
        private static readonly string _func;

        private const string StartHostCommand = "start --build";

        static CliTester()
        {
            _func = Environment.GetEnvironmentVariable("FUNC_PATH");

            if (_func == null)
            {
                // Fallback for local testing in Visual Studio, etc.
                _func = Path.Combine(Environment.CurrentDirectory, "func");

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _func += ".exe";
                }

                if (!File.Exists(_func))
                {
                    throw new ApplicationException("Could not locate the 'func' executable to use for testing. Make sure the FUNC_PATH environment variable is set to the full path of the func executable.");
                }
            }
        }

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
            await using var hostExe = new Executable(_func, StartHostCommand, workingDirectory: workingDir);
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            foreach (var runConfiguration in runConfigurations)
            {
                Task hostTask = startHost ? hostExe.RunAsync(logStd, logErr) : Task.Delay(runConfiguration.CommandTimeout);
                stdout.Clear();
                stderr.Clear();
                var exitCode = 0;
                var exitError = false;
                runConfiguration.PreTest?.Invoke(workingDir);

                for (var i = 0; i < runConfiguration.Commands.Length; i++)
                {
                    var command = runConfiguration.Commands[i];

                    await using var exe = command switch
                    {
                        string cmd when cmd.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase) =>
                            new Executable("dotnet", command.Substring(7), workingDirectory: workingDir),

                        // default to func
                        _ => new Executable(_func, command, workingDirectory: workingDir)
                    };

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
                        exitCode = await exe.RunAsync(logStd, logErr, timeout: runConfiguration.CommandTimeout);
                    }

                    if (!runConfiguration.ExpectExit && runConfiguration.Test is not null)
                    {
                        var exitCodeTask = exe.RunAsync(logStd, logErr);

                        if (runConfiguration.WaitForRunningHostState)
                        {
                            await ProcessHelper.WaitForFunctionHostToStart(exe.Process, runConfiguration.HostProcessPort);
                        }

                        try
                        {
                            await runConfiguration.Test.Invoke(workingDir, exe.Process, stdout);
                        }
                        catch (Exception e)
                        {
                            logErr($"Error while running test: {e.Message}");
                        }
                        finally
                        {
                            await Task.WhenAny(exitCodeTask, Task.Delay(runConfiguration.CommandTimeout));
                            if (!exitCodeTask.IsCompleted)
                            {
                                exe.Process.Kill();
                                logErr("Expected process to exit after calling Test() and within timeout, but it didn't.");
                            }
                            else
                            {
                                exitCode = exitCodeTask.Result;
                            }
                        }
                    }

                    // -1 means we intentionally killed the process via p.Kill() - this is not an error
                    exitError |= exitCode != 0 && exitCode != -1;
                }

                if (runConfiguration.ExpectExit && runConfiguration.Test != null)
                {
                    await runConfiguration.Test.Invoke(workingDir, null, null);
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
