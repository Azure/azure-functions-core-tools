// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Python)]
    public class PythonInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("")]
        [InlineData("v1")]
        [InlineData("v2")]
        public void Init_Python_App_With_Supported_Model(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var programmingModelFlag = string.IsNullOrEmpty(programmingModel) ? string.Empty : $"--model {programmingModel}";
            var testName = nameof(Init_Python_App_With_Supported_Model);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var expectedcontent = new[] { "FUNCTIONS_WORKER_RUNTIME", "python" };
            var requirementsPath = Path.Combine(workingDir, "requirements.txt");
            var expectedRequirementsContent = new[] { "# Do not include azure-functions-worker", "azure-functions" };

            // Initialize python function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "python", programmingModelFlag]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            if (programmingModel == string.Empty || programmingModel == "v2")
            {
                funcInitResult.Should().HaveStdOutContaining($"Writing function_app.py");
            }

            if (programmingModel == "v1")
            {
                funcInitResult.Should().HaveStdOutContaining($"Writing getting_started.md");
            }

            funcInitResult.Should().NotHaveStdOutContaining($"Initialized empty Git repository");
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedcontent);
            funcInitResult.Should().FileExistsWithContent(requirementsPath, expectedRequirementsContent);
        }

        [Theory]
        [InlineData("v3")]
        public void Init_Python_App_With_UnsupportedModel(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Python_App_With_UnsupportedModel);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "python", "--model", programmingModel]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"programming model is not supported for worker runtime python");
        }

        [Fact]
        public void Init_Python_App_With_Dockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Python_App_With_Dockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { "FROM mcr.microsoft.com/azure-functions/python:4" };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "python", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public async Task Init_Python_App_With_Docker_Only_For_Existing_Project()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Python_App_With_Docker_Only_For_Existing_Project);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/python:4" };

            // Initialize dotnet function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "python"]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public async void Init_Python_App_Twice()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Python_App_Twice);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize dotnet function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "python"]);

            // Initialize python function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "python"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"requirements.txt already exists. Skipped!");
            funcInitResult.Should().HaveStdOutContaining($"host.json already exists. Skipped!");
            funcInitResult.Should().HaveStdOutContaining($".gitignore already exists. Skipped!");
        }

        [Fact]
        public void Init_Python_App_ModelV1_Generates_getting_started_md()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Python_App_ModelV1_Generates_getting_started_md);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var gettingStartedPath = Path.Combine(workingDir, "getting_started.md");
            var expectedcontent = new[] { "## Getting Started with Azure Function" };

            // Initialize python function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "python", "-m V1"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing getting_started.md");
            funcInitResult.Should().FileExistsWithContent(gettingStartedPath, expectedcontent);
        }
    }
}
