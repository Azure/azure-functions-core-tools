// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

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

            BasePackTests.TestBasicPackFunctionality(
                PythonProjectPath,
                testName,
                FuncPath,
                Log,
                new[]
                {
                    "host.json",
                    "requirements.txt",
                    "function_app.py"
                });
        }

        [Fact]
        public void Pack_PythonFromCache_WorksAsExpected()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Pack_PythonFromCache_WorksAsExpected);
            var syncDirMessage = "Directory .python_packages already in sync with requirements.txt. Skipping restoring dependencies...";

            // Step 1: Initialize a Python function app
            // Note that we need to initialize the function app as we are testing an instance that has not run pack before.
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

            // Step 3: Run pack again without changing requirements.txt (should use cache)
            var secondPackResult = funcPackCommand
                .WithWorkingDirectory(workingDir)
                .Execute([]);

            secondPackResult.Should().ExitWith(0);
            secondPackResult.Should().HaveStdOutContaining("Creating a new package");
            secondPackResult.Should().NotHaveStdOutContaining(syncDirMessage);

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
        }
    }
}
