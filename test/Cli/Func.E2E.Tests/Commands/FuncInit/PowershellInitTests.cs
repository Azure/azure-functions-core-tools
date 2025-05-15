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
        public void Init_App_With_Powershell_Runtime()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_App_With_Powershell_Runtime);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var expectedcontent = new[] { "FUNCTIONS_WORKER_RUNTIME", "powershell" };
            var profileFilePath = Path.Combine(workingDir, "profile.ps1");
            var expectedProfileContent = new[] { $"# Azure Functions profile.ps1" };

            // Initialize powershell function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "powershell"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing {WorkingDirectory}\\.vscode\\extensions.json");
            funcInitResult.Should().NotHaveStdOutContaining($"Initialized empty Git repository");
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedcontent);
            funcInitResult.Should().FileExistsWithContent(profileFilePath, expectedProfileContent);
        }

        [Fact]
        public void Init_Powershell_App_With_DockerFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Powershell_App_With_DockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerFileContent = new[] { "FROM mcr.microsoft.com/azure-functions/powershell:4" };

            // Initialize powershell function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "powershell", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerFileContent);
        }

        [Fact]
        public async Task Init_Powershell_App_With_Docker_Only_For_Existing_Project()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Powershell_App_With_Docker_Only_For_Existing_Project);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/powershell:4" };

            // Initialize dotnet function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "powershell"]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public void Init_Powershell_App_With_Enable_Managed_Dependencies()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Powershell_App_With_Enable_Managed_Dependencies);
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
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var expectedLocalSettingsContent = new[]
                        {
                            "FUNCTIONS_WORKER_RUNTIME",
                            "powershell",
                            "FUNCTIONS_WORKER_RUNTIME_VERSION",
                            "7.4"
                        };

            // Initialize powershell function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "powershell", "--managed-dependencies"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().FileExistsWithContent(hostJsonfilePath, expectedHostJsonContent);
            funcInitResult.Should().FileExistsWithContent(requirementsFilePath, expectedRequirementsFileContent);
            funcInitResult.Should().FileExistsWithContent(profileFilePath, expectedProfileContent);
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedLocalSettingsContent);
        }
    }
}
