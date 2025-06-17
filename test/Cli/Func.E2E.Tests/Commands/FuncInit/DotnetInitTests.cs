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
        public void Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles()
        {
            var testName = nameof(Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles);

            var localSettingsPath = Path.Combine(WorkingDirectory, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize dotnet function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var funcInitResult = funcInitCommand
               .WithWorkingDirectory(WorkingDirectory)
               .Execute(["--worker-runtime", "dotnet"]);

            // Validate expected output content
            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(WorkingDirectory);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net8.0")]
        public void Init_WithSupportedTargetFramework_GeneratesProjectFile_ContainsExpectedVersion(string targetFramework)
        {
            var testName = nameof(Init_WithSupportedTargetFramework_GeneratesProjectFile_ContainsExpectedVersion);

            var projectName = $"dotnet-funcs-{targetFramework[..^2]}"; // trim last two characters (e.g., "net6.0" to "net6")
            var localSettingsPath = Path.Combine(WorkingDirectory, projectName, Common.Constants.LocalSettingsJsonFileName);
            var csprojFilePath = Path.Combine(WorkingDirectory, projectName, $"{projectName}.csproj");

            var expectedLocalSettingsContent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet" };
            var expectedCsprojContent = new[] { "Microsoft.NET.Sdk", "v4", targetFramework };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedLocalSettingsContent),
                (csprojFilePath, expectedCsprojContent)
            };

            // Initialize dotnet function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([projectName, "--worker-runtime", "dotnet", "--target-framework", targetFramework]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithUnsupportedTargetFramework_FailsWithError()
        {
            var testName = nameof(Init_WithUnsupportedTargetFramework_FailsWithError);

            string unsupportedTargetFramework = "net7.0";
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize dotnet function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["dotnet-funcs", "--worker-runtime", "dotnet", "--target-framework", unsupportedTargetFramework]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"Unable to parse target framework {unsupportedTargetFramework} for worker runtime dotnet. Valid options are net8.0, net6.0");
        }

        [Theory]
        [InlineData("")]
        [InlineData("--csx")]
        public void Init_WithDockerFlagAndCsx_GeneratesDockerFile(string csxParam)
        {
            var testName = nameof(Init_WithDockerFlagAndCsx_GeneratesDockerFile);

            var dockerFilePath = Path.Combine(WorkingDirectory, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--worker-runtime", "dotnet", "--docker", csxParam]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithCsxFlag_GeneratesExpectedFunctionProjectFiles()
        {
            var testName = nameof(Init_WithCsxFlag_GeneratesExpectedFunctionProjectFiles);

            var localSettingsPath = Path.Combine(WorkingDirectory, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize dotnet function app
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--worker-runtime", "dotnet", "--csx"]);

            // Validate expected output content
            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(WorkingDirectory);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_DockerOnlyOnExistingProject_GeneratesDockerfile()
        {
            var testName = nameof(Init_DockerOnlyOnExistingProject_GeneratesDockerfile);

            var dockerFilePath = Path.Combine(WorkingDirectory, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet"]);

            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_DockerOnlyOnExistingCsxProject_GeneratesDockerfile()
        {
            var testName = nameof(Init_DockerOnlyOnExistingCsxProject_GeneratesDockerfile);

            var dockerFilePath = Path.Combine(WorkingDirectory, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet function app with retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet", "--csx"]);

            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--docker-only", "--csx"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}
