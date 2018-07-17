﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Functions.Cli.Common;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.Tests.E2E.Helpers
{
    public static class CliTester
    {
        private static string _func = System.Environment.GetEnvironmentVariable("FUNC_PATH");
        public static Task Run(RunConfiguration runConfiguration, ITestOutputHelper output) => Run(new[] { runConfiguration }, output);

        public static async Task Run(RunConfiguration[] runConfigurations, ITestOutputHelper output)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetTempFileName().Replace(".", ""));
            Directory.CreateDirectory(tempDir);
            try
            {
                await InternalRun(tempDir, runConfigurations, output);
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch { }
            }
        }

        private static async Task InternalRun(string workingDir, RunConfiguration[] runConfigurations, ITestOutputHelper output)
        {
            foreach (var runConfiguration in runConfigurations)
            {
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var exitError = false;

                for (var i = 0; i < runConfiguration.Commands.Length; i++)
                {
                    var command = runConfiguration.Commands[i];
                    var exe = new Executable(_func, command, workingDirectory: workingDir);
                    output.WriteLine($"Running: > {exe.Command}");
                    if (runConfiguration.ExpectExit || (i + 1) < runConfiguration.Commands.Length)
                    {
                        var exitCode = await exe.RunAsync(logStd, logErr, timeout: runConfiguration.CommandTimeout);
                        exitError &= exitCode != 1;
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
                                exitError &= (await exitCodeTask) != 1;
                            }
                        }
                    }
                }

                if (runConfiguration.ExpectExit && runConfiguration.Test != null)
                {
                    await runConfiguration.Test.Invoke(workingDir, null);
                }

                AssertExitError(runConfiguration, exitError);
                // AssertHasStandardError(runConfiguration, stderr);
                AssertFiles(runConfiguration, workingDir);
                AssertDirectories(runConfiguration, workingDir);
                AssertOutputContent(runConfiguration, stdout);
                AssertErrorContent(runConfiguration, stderr);

                void logStd(string line)
                {
                    stdout.AppendLine(line);
                    output.WriteLine($"stdout: {line}");
                }

                void logErr(string line)
                {
                    stderr.AppendLine(line);
                    output.WriteLine($"stderr: {line}");
                }
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
                    else
                    {
                        fileContent
                            .Should()
                            .ContainAll(fileResult.ContentContains, because: $"File ({fileResult.Name}) content should contain all in ContentContains");
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
