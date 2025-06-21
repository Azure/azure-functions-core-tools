// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Powershell)]
    public class PowershellInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Init_PowershellApp_GeneratesExpectedFunctionProjectFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_PowershellApp_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "powershell" };
            var profileFilePath = Path.Combine(workingDir, "profile.ps1");
            var expectedProfileContent = new[] { $"# Azure Functions profile.ps1" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent),
                (profileFilePath, expectedProfileContent)
            };

            // Initialize powershell function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "powershell"]);

            // Validate expected output content
            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithDockerFlag_GeneratesDockerFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDockerFlag_GeneratesDockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerFileContent = new[] { "FROM mcr.microsoft.com/azure-functions/powershell:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerFileContent)
            };

            // Initialize powershell function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "powershell", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_DockerOnlyOnExistingProject_GeneratesDockerFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DockerOnlyOnExistingProject_GeneratesDockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/powershell:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet function app with retry helper
            _ = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "powershell"]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithManagedDependencies_GeneratesAllExpectedConfigFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithManagedDependencies_GeneratesAllExpectedConfigFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var hostJsonfilePath = Path.Combine(workingDir, "host.json");
            var expectedHostJsonContent = new[]
                        {
                            "logging",
                            "applicationInsights",
                            "extensionBundle",
                            "managedDependency",
                            "enabled",
                            "true"
                        };
            var requirementsFilePath = Path.Combine(workingDir, "requirements.psd1");
            var expectedRequirementsFileContent = new[]
                        {
                            "For latest supported version, go to 'https://www.powershellgallery.com/packages/Az'.",
                            "To use the Az module in your function app, please uncomment the line below.",
                            "Az",
                        };
            var profileFilePath = Path.Combine(workingDir, "profile.ps1");
            var expectedProfileContent = new[]
                        {
                            "env:MSI_SECRET",
                            "Disable-AzContextAutosave -Scope Process | Out-Null",
                            "Connect-AzAccount -Identity"
                        };
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedLocalSettingsContent = new[]
                        {
                            Common.Constants.FunctionsWorkerRuntime,
                            "powershell",
                            "FUNCTIONS_WORKER_RUNTIME_VERSION",
                            "7.4"
                        };

            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedLocalSettingsContent),
                (profileFilePath, expectedProfileContent),
                (hostJsonfilePath, expectedHostJsonContent),
                (requirementsFilePath, expectedRequirementsFileContent)
            };

            // Initialize powershell function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "powershell", "--managed-dependencies"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}
