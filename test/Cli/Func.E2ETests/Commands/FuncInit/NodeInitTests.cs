// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodeInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("")]
        [InlineData("v3")]
        [InlineData("v4")]
        public void Init_WithSupportedModel_GeneratesExpectedFunctionProjectFiles(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var programmingModelFlag = string.IsNullOrEmpty(programmingModel) ? string.Empty : $"--model {programmingModel}";
            var testName = nameof(Init_WithSupportedModel_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "node" };
            var packageJsonPath = Path.Combine(workingDir, Common.Constants.PackageJsonFileName);
            var expectedJsoncontent = new[] { $"\"@azure/functions\": \"^4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };
            if (programmingModel == "v4" || string.IsNullOrEmpty(programmingModel))
            {
                filesToValidate.Add((packageJsonPath, expectedJsoncontent));
            }

            // Initialize node function app
            var funcInitResult = funcInitCommand
               .WithWorkingDirectory(workingDir)
               .Execute(["--worker-runtime", "node", programmingModelFlag]);

            // Validate expected output content
            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("v1")]
        [InlineData("v2")]
        public void Init_WithUnsupportedModel_FailsWithError(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithUnsupportedModel_FailsWithError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node", "--model", programmingModel]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"The {programmingModel} programming model is not supported for worker runtime node.");
        }

        [Fact]
        public void Init_GeneratesHostJson_ContainsExpectedLoggingConfig()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_GeneratesHostJson_ContainsExpectedLoggingConfig);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var hostJsonFilePath = Path.Combine(workingDir, Common.Constants.HostJsonFileName);
            var expectedHostJsonContent = new[] { "logging", "applicationInsights", "excludedTypes", "Request" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (hostJsonFilePath, expectedHostJsonContent)
            };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing host.json");
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithDockerFlag_GeneratesDockerFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDockerFlag_GeneratesDockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { "FROM mcr.microsoft.com/azure-functions/node:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithTypescriptAndDockerFlag_GeneratesDockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithTypescriptAndDockerFlag_GeneratesDockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "node" };
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { "FROM mcr.microsoft.com/azure-functions/node:4", "npm run build" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent),
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node", "--language", "typescript", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async void Init_DockerOnlyOnExistingProject_GeneratesDockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DockerOnlyOnExistingProject_GeneratesDockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/node:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize node function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node"]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only", "--worker-runtime", "node"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithSkipNpmInstallFlag_SuccessfullySkipsNpmInstall()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithSkipNpmInstallFlag_SuccessfullySkipsNpmInstall);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node", "--skip-npm-install"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"You skipped \"npm install\". You must run \"npm install\" manually");
            funcInitResult.Should().NotHaveStdOutContaining($"Running 'npm install'...");
        }

        [Fact]
        public void Init_Typescript_ModelV4_SkipNpmInstall_SuccessfullySkipsNpmInstall()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Typescript_ModelV4_SkipNpmInstall_SuccessfullySkipsNpmInstall);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node", "--language", "typescript", "--model", "V4", "--skip-npm-install"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"You skipped \"npm install\". You must run \"npm install\" manually");
            funcInitResult.Should().NotHaveStdOutContaining($"Running 'npm install'...");
        }
    }
}
