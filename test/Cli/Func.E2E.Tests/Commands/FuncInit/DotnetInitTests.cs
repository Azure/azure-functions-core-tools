// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Init_App_With_Dotnet_Runtime()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_App_With_Dotnet_Runtime);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var expectedcontent = new[] { "FUNCTIONS_WORKER_RUNTIME", "dotnet" };

            // Initialize dotnet function app
            var funcInitResult = funcInitCommand
               .WithWorkingDirectory(workingDir)
               .Execute(["--worker-runtime", "dotnet"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing {WorkingDirectory}\\.vscode\\extensions.json");
            funcInitResult.Should().NotHaveStdOutContaining($"Initialized empty Git repository");
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedcontent);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net8.0")]
        public void Init_Dotnet_App_With_Supported_TargetFramework(string targetFramework)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Dotnet_App_With_Supported_TargetFramework);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "dotnet-funcs", "local.settings.json");
            var expectedLocalSettingsContent = new[] { "FUNCTIONS_WORKER_RUNTIME", "dotnet" };
            var csprojfilepath = Path.Combine(workingDir, "dotnet-funcs", "dotnet-funcs.csproj");
            var expectedCsprojContent = new[] { "Microsoft.NET.Sdk", "v4", targetFramework };

            // Initialize dotnet function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["dotnet-funcs", "--worker-runtime", "dotnet", "--target-framework", targetFramework]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedLocalSettingsContent);
            funcInitResult.Should().FileExistsWithContent(csprojfilepath, expectedCsprojContent);
        }

        [Fact]
        public void Init_Dotnet_App_With_UnSupported_TargetFramework()
        {
            string unsupportedTargetFramework = "net7.0";
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Dotnet_App_With_UnSupported_TargetFramework);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize dotnet function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["dotnet-funcs", "--worker-runtime", "dotnet", "--target-framework", unsupportedTargetFramework]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"Unable to parse target framework {unsupportedTargetFramework} for worker runtime dotnet. Valid options are net8.0, net6.0");
        }

        [Fact]
        public void Init_Dotnet_App_DockerFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Dotnet_App_DockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "dotnet", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public void Init_Dotnet_App_With_Dockerfile_for_csx()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Dotnet_App_With_Dockerfile_for_csx);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };

            // Initialize node function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "dotnet", "--docker", "--csx"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public void Init_Dotnet_App_With_CSX()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Dotnet_App_With_CSX);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "local.settings.json");
            var expectedcontent = new[] { "FUNCTIONS_WORKER_RUNTIME", "dotnet" };

            // Initialize dotnet function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "dotnet", "--csx"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing {WorkingDirectory}\\.vscode\\extensions.json");
            funcInitResult.Should().NotHaveStdOutContaining($"Initialized empty Git repository");
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedcontent);
        }

        [Fact]
        public async Task Init_Dotnet_App_With_Docker_Only_For_Existing_Project()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Dotnet_App_With_Docker_Only_For_Existing_Project);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };

            // Initialize dotnet function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet"]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Fact]
        public async Task Init_Dotnet_App_With_Docker_Only_For_csx_Project()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Dotnet_App_With_Docker_Only_For_csx_Project);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };

            // Initialize dotnet function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet", "--csx"]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only", "--csx"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }
    }
}
