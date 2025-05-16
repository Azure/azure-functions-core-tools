// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodeInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("")]
        [InlineData("v3")]
        [InlineData("v4")]
        public void Init_Node_App_With_SupportedModel(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var programmingModelFlag = string.IsNullOrEmpty(programmingModel) ? string.Empty : $"--model {programmingModel}";
            var testName = nameof(Init_Node_App_With_SupportedModel);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var expectedcontent = new[] { "FUNCTIONS_WORKER_RUNTIME", "node" };
            var packageJsonPath = Path.Combine(workingDir, "package.json");
            var expectedJsoncontent = new[] { $"\"@azure/functions\": \"^4" };

            // Initialize node function app
            var funcInitResult = funcInitCommand
               .WithWorkingDirectory(workingDir)
               .Execute(["--worker-runtime", "node", programmingModelFlag]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().NotHaveStdOutContaining($"Initialized empty Git repository");
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedcontent);

            if (programmingModel == "v4" || programmingModel == string.Empty)
            {
                funcInitResult.Should().FileExistsWithContent(packageJsonPath, expectedJsoncontent);
            }
        }

        [Theory]
        [InlineData("v1")]
        [InlineData("v2")]
        public void Init_Node_App_With_UnsupportedModel(string programmingModel)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Node_App_With_UnsupportedModel);
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
        public void Init_Node_App_Contains_Logging_Config()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Node_App_Contains_Logging_Config);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var hostJsonFilePath = Path.Combine(workingDir, "host.json");
            var expectedHostJsonContent = new[] { "logging", "applicationInsights", "excludedTypes", "Request" };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing host.json");
            funcInitResult.Should().FileExistsWithContent(hostJsonFilePath, expectedHostJsonContent);
        }

        [Fact]
        public void Init_Node_App_With_Dockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Node_App_With_Dockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { "FROM mcr.microsoft.com/azure-functions/node:4" };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public void Init_Node_App_Typescript_With_Dockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Node_App_Typescript_With_Dockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var expectedcontent = new[] { "FUNCTIONS_WORKER_RUNTIME", "node" };
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { "FROM mcr.microsoft.com/azure-functions/node:4", "npm run build" };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "node", "--language", "typescript", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedcontent);
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public async void Init_Docker_Only_For_Existing_Project_Node()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Docker_Only_For_Existing_Project_Node);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/node:4" };

            // Initialize node function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node"]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public void Init_Node_App_With_skip_npm_install()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Node_App_With_skip_npm_install);
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
        public void Init_Node_App_Typescript_With_ModelV4_skip_npm_install()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Node_App_Typescript_With_ModelV4_skip_npm_install);
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
