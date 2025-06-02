// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading.Tasks;
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
        public async Task Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(WorkingDirectory, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize dotnet-isolated function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "dotnet-isolated"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_WithNet9TargetFramework_GeneratesProjectFile_ContainsExpectedVersion()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithNet9TargetFramework_GeneratesProjectFile_ContainsExpectedVersion);
            var projectName = "Test-funcs";
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, projectName, Common.Constants.LocalSettingsJsonFileName);
            var csprojfilepath = Path.Combine(workingDir, projectName, "Test-funcs.csproj");
            var expectedLocalSettingsContent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated" };
            var expectedCsprojContent = new[] { "Microsoft.NET.Sdk", "v4", "net9.0" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedLocalSettingsContent),
                (csprojfilepath, expectedCsprojContent)
            };

            // Initialize dotnet-isolated function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, [projectName, "--worker-runtime", "dotnet-isolated", "--target-framework", "net9.0"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_WithDotnetIsolatedAndDockerFlag_GeneratesDockerFile()
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
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "dotnet-isolated", "--docker"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net7.0")]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        public async Task Init_WithTargetFrameworkAndDockerFlag_GeneratesDockerFile(string targetFramework)
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
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework, "--docker"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net7.0")]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        public async Task Init_DockerOnlyOnExistingProjectWithTargetFramework_GeneratesDockerfile(string targetFramework)
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
            var initialResult = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework]);
            Assert.NotNull(initialResult);

            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--docker-only"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}
