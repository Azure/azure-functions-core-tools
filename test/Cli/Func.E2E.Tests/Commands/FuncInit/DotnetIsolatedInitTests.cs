// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    public class DotnetIsolatedInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Init_App_With_DotnetIsolated_Runtime()
        {
            var testName = nameof(Init_App_With_DotnetIsolated_Runtime);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(WorkingDirectory, "local.settings.json");
            var expectedcontent = new[] { "FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated" };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
               .WithWorkingDirectory(WorkingDirectory)
               .Execute(["--worker-runtime", "dotnet-isolated"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing {WorkingDirectory}\\.vscode\\extensions.json");
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedcontent);
        }

        [Fact]
        public void Init_DotnetIsolated_With_Net9TargetFramework()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DotnetIsolated_With_Net9TargetFramework);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, "Test-funcs", "local.settings.json");
            var csprojfilepath = Path.Combine(workingDir, "Test-funcs", "Test-funcs.csproj");
            var expectedLocalSettingsContent = new[] { "FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated" };
            var expectedCsprojContent = new[] { "Microsoft.NET.Sdk", "v4", "net9.0" };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["Test-funcs", "--worker-runtime", "dotnet-isolated", "--target-framework", "net9.0"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().FileExistsWithContent(localSettingsPath, expectedLocalSettingsContent);
            funcInitResult.Should().FileExistsWithContent(csprojfilepath, expectedCsprojContent);
        }

        [Fact]
        public void Init_DotnetIsolated_With_Dockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DotnetIsolated_With_Dockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0" };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "dotnet-isolated", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net7.0")]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        public void Init_DotnetIsolated_With_TargetFramework_Dockerfile(string targetFramework)
        {
            var targetFrameworkstr = targetFramework.Replace("net", string.Empty);
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DotnetIsolated_With_TargetFramework_Dockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated{targetFrameworkstr}" };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework, "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net7.0")]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        public async void Init_Docker_Only_For_Existing_Project_With_TargetFramework(string targetFramework)
        {
            var targetFrameworkstr = targetFramework.Replace("net", string.Empty);
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_Docker_Only_For_Existing_Project_With_TargetFramework);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated{targetFrameworkstr}" };

            // Initialize dotnet-isolated function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining($"Writing Dockerfile");
            funcInitResult.Should().FileExistsWithContent(dockerFilePath, expectedDockerfileContent);
        }
    }
}
