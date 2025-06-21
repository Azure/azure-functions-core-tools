// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading.Tasks;
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
        public async Task Init_With_SupportedModel_GeneratesExpectedFunctionProjectFiles(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_With_SupportedModel_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "python" };
            var requirementsPath = Path.Combine(workingDir, "requirements.txt");
            var expectedRequirementsContent = new[] { "# Do not include azure-functions-worker", "azure-functions" };

            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent),
                (requirementsPath, expectedRequirementsContent)
            };

            // Build arguments list based on programming model
            var args = new List<string> { "--worker-runtime", "python" };
            if (!string.IsNullOrEmpty(programmingModel))
            {
                args.AddRange(["--model", programmingModel]);
            }

            // Initialize python function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, args);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);

            if (programmingModel == string.Empty || programmingModel == "v2")
            {
                funcInitResult?.Should().HaveStdOutContaining($"Writing function_app.py");
            }

            if (programmingModel == "v1")
            {
                funcInitResult?.Should().HaveStdOutContaining($"Writing getting_started.md");
            }
        }

        [Theory]
        [InlineData("v3")]
        public async Task Init_With_UnsupportedModel_FailsWithError(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_With_UnsupportedModel_FailsWithError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize python function app with unsupported model
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "python", "--model", programmingModel], exitWith: 1);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(1);
            funcInitResult?.Should().HaveStdErrContaining($"programming model is not supported for worker runtime python");
        }

        [Fact]
        public async Task Init_WithDockerFlag_GeneratesDockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDockerFlag_GeneratesDockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { "FROM mcr.microsoft.com/azure-functions/python:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize python function app with docker flag
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "python", "--docker"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_DockerOnlyOnExistingProject_GeneratesDockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DockerOnlyOnExistingProject_GeneratesDockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/python:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize python function app with retry helper
            var initialResult = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "python"]);
            Assert.NotNull(initialResult);

            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--docker-only"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_WithExistingProject_SkipsExistingFilesCreation()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithExistingProject_SkipsExistingFilesCreation);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize python function app with retry helper
            var initialResult = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "python"]);
            Assert.NotNull(initialResult);

            // Initialize python function app again to test skipping existing files
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "python"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().HaveStdOutContaining($"requirements.txt already exists. Skipped!");
            funcInitResult?.Should().HaveStdOutContaining($"host.json already exists. Skipped!");
            funcInitResult?.Should().HaveStdOutContaining($".gitignore already exists. Skipped!");
        }

        [Fact]
        public async Task Init_PythonApp_ModelV1_GeneratesGettingStartedDoc()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_PythonApp_ModelV1_GeneratesGettingStartedDoc);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var gettingStartedPath = Path.Combine(workingDir, "getting_started.md");
            var expectedcontent = new[] { "## Getting Started with Azure Function" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (gettingStartedPath, expectedcontent)
            };

            // Initialize python function app with model v1
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "python", "-m", "V1"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().HaveStdOutContaining($"Writing getting_started.md");
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}
