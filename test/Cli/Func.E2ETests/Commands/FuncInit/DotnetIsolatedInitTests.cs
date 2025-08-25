// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
    [Collection("Dotnet-isolated func init tests")] // Runtests in this class sequentially to avoid conflicts for templating
    public class DotnetIsolatedInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles()
        {
            var workinDir = WorkingDirectory;
            var testName = nameof(Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(WorkingDirectory, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
               .WithWorkingDirectory(workinDir)
               .Execute(["--worker-runtime", "dotnet-isolated"]);

            // Validate expected output content
            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workinDir);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        [InlineData("net10.0")]
        public void Init_WithNetTargetFramework_GeneratesProjectFile_ContainsExpectedVersion(string targetFramework)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithNetTargetFramework_GeneratesProjectFile_ContainsExpectedVersion);
            var projectName = "Test-funcs";
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, projectName, Common.Constants.LocalSettingsJsonFileName);
            var csprojfilepath = Path.Combine(workingDir, projectName, "Test-funcs.csproj");
            var expectedLocalSettingsContent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated" };
            var expectedCsprojContent = new[] { "Microsoft.NET.Sdk", "v4", targetFramework };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedLocalSettingsContent),
                (csprojfilepath, expectedCsprojContent)
            };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute([projectName, "--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public void Init_WithDotnetIsolatedAndDockerFlag_GeneratesDockerFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDotnetIsolatedAndDockerFlag_GeneratesDockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "dotnet-isolated", "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net7.0")]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        public void Init_WithTargetFrameworkAndDockerFlag_GeneratesDockerFile(string targetFramework)
        {
            var targetFrameworkstr = targetFramework.Replace("net", string.Empty);
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithTargetFrameworkAndDockerFlag_GeneratesDockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated{targetFrameworkstr}" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet-isolated function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework, "--docker"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net7.0")]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        [InlineData("net10.0")]
        public async void Init_DockerOnlyOnExistingProjectWithTargetFramework_GeneratesDockerfile(string targetFramework)
        {
            var targetFrameworkstr = targetFramework.Replace("net", string.Empty);
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DockerOnlyOnExistingProjectWithTargetFramework_GeneratesDockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated{targetFrameworkstr}" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet-isolated function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework]);

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}
