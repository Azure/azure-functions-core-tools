// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Compression;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncPack
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Python)]
    public class PythonPackTests : BaseE2ETests
    {
        public PythonPackTests(ITestOutputHelper log)
            : base(log)
        {
        }

        private string PythonProjectPath => Path.Combine(TestProjectDirectory, "TestPythonProject");

        [Fact]
        public void Pack_Python_WorksAsExpected()
        {
            var testName = nameof(Pack_Python_WorksAsExpected);

            // Remove existing _python_packages directory
            if (Directory.Exists(Path.Combine(PythonProjectPath, ".python_packages")))
            {
                Directory.Delete(Path.Combine(PythonProjectPath, ".python_packages"), true);
            }

            var logsToValidate = new[]
            {
                "Found Python version 3.11.9 (py).",
                "Successfully downloaded azure-functions werkzeug MarkupSafe"
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                logsToValidate = logsToValidate.Append("Python function apps is supported only on Linux. Please use the --build-native-deps flag when building on windows to ensure dependencies are properly restored.").ToArray();
            }

            BasePackTests.TestBasicPackFunctionality(
                PythonProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "requirements.txt",
                    "function_app.py",
                    Path.Combine(".python_packages", "requirements.txt.md5")
                },
                logsToValidate);
        }

        [Fact]
        public void Pack_PythonFromCache_WorksAsExpected()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_PythonFromCache_WorksAsExpected);
            var syncDirMessage = "Directory .python_packages already in sync with requirements.txt. Skipping restoring dependencies...";

            // Step 1: Initialize a Python function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var initResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "python"]);

            initResult.Should().ExitWith(0);
            initResult.Should().HaveStdOutContaining("Writing host.json");
            initResult.Should().HaveStdOutContaining("Writing local.settings.json");

            // Create HTTP trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log);
            var newResult = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "\"HTTP Trigger\"", "--name", "httptrigger", "--authlevel", "anonymous"]);

            newResult.Should().ExitWith(0);

            // Verify local.settings.json has the correct content
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, new[] { "FUNCTIONS_WORKER_RUNTIME", "python" })
            };
            initResult.Should().FilesExistsWithExpectContent(filesToValidate);

            // Step 2: Run pack for the first time
            var funcPackCommand = new FuncPackCommand(FuncPath, testName, Log);
            var firstPackResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            firstPackResult.Should().ExitWith(0);
            firstPackResult.Should().HaveStdOutContaining("Creating a new package");
            firstPackResult.Should().NotHaveStdOutContaining(syncDirMessage);

            // Verify .python_packages/requirements.txt.md5 file exists
            var pythonPackagesMd5Path = Path.Combine(workingDir, ".python_packages", "requirements.txt.md5");
            var packFilesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (pythonPackagesMd5Path, new[] { string.Empty }) // Just check file exists, content can be empty
            };
            firstPackResult.Should().FilesExistsWithExpectContent(packFilesToValidate);

            // Step 3: Run pack again without changing requirements.txt (should use cache)
            var secondPackResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            secondPackResult.Should().ExitWith(0);
            secondPackResult.Should().HaveStdOutContaining("Creating a new package");
            secondPackResult.Should().HaveStdOutContaining(syncDirMessage);

            // Step 4: Update requirements.txt and pack again (should restore dependencies)
            var requirementsPath = Path.Combine(workingDir, "requirements.txt");
            Log.WriteLine($"Writing to file {requirementsPath}");
            File.WriteAllText(requirementsPath, "requests");

            var thirdPackResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            thirdPackResult.Should().ExitWith(0);
            thirdPackResult.Should().HaveStdOutContaining("Creating a new package");
            thirdPackResult.Should().NotHaveStdOutContaining(syncDirMessage);

            // Verify .python_packages/requirements.txt.md5 file still exists
            thirdPackResult.Should().FilesExistsWithExpectContent(packFilesToValidate);
        }

        [Fact]
        public void Pack_Python_BuildNativeDeps_OnWindows_WorksAsExpected()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Only validate this scenario on Windows
                return;
            }

            var testName = nameof(Pack_Python_BuildNativeDeps_OnWindows_WorksAsExpected);

            if (Directory.Exists(Path.Combine(PythonProjectPath, ".python_packages")))
            {
                Directory.Delete(Path.Combine(PythonProjectPath, ".python_packages"), true);
            }

            var packResult = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(PythonProjectPath)
                .Execute(["--build-native-deps"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");

            var zipFiles = Directory.GetFiles(PythonProjectPath, "*.zip");
            Assert.True(zipFiles.Length > 0, $"No zip files found in {PythonProjectPath}");

            packResult.Should().ValidateZipContents(
                zipFiles.First(),
                new[]
                {
                    "host.json",
                    "requirements.txt",
                    "function_app.py",
                    Path.Combine(".python_packages", "requirements.txt.md5")
                },
                Log);

            File.Delete(zipFiles.First());
        }

        [Fact]
        public void Pack_Python_NoBuild_JustZipsDirectory()
        {
            var testName = nameof(Pack_Python_NoBuild_JustZipsDirectory);

            var packResult = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(PythonProjectPath)
                .Execute(["--no-build"]);

            packResult.Should().ExitWith(0);
            packResult.Should().HaveStdOutContaining("Creating a new package");
            packResult.Should().HaveStdOutContaining("Skipping build event for functions project (--no-build).");

            var zipFiles = Directory.GetFiles(PythonProjectPath, "*.zip");
            Assert.True(zipFiles.Length > 0, $"No zip files found in {PythonProjectPath}");

            packResult.Should().ValidateZipContents(
                zipFiles.First(),
                new[]
                {
                    "host.json",
                    "requirements.txt"
                },
                Log);

            File.Delete(zipFiles.First());
        }

        [Fact]
        public void Pack_Python_NoBuild_WithNativeDeps_ShouldFail()
        {
            var testName = nameof(Pack_Python_NoBuild_WithNativeDeps_ShouldFail);

            var packResult = new FuncPackCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(PythonProjectPath)
                .Execute(["--no-build", "--build-native-deps"]);

            packResult.Should().ExitWith(1);
            packResult.Should().HaveStdErrContaining("Invalid options: --no-build cannot be used with --build-native-deps.");
        }
    }
}
