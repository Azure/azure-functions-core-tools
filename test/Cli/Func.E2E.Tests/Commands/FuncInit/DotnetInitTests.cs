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
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public async Task Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithWorkerRuntime_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize dotnet function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "dotnet"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Theory]
        [InlineData("net6.0")]
        [InlineData("net8.0")]
        public async Task Init_WithSupportedTargetFramework_GeneratesProjectFile_ContainsExpectedVersion(string targetFramework)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithSupportedTargetFramework_GeneratesProjectFile_ContainsExpectedVersion);
            var projectName = "dotnet-funcs";
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, projectName, Common.Constants.LocalSettingsJsonFileName);
            var expectedLocalSettingsContent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet" };
            var csprojfilepath = Path.Combine(workingDir, projectName, "dotnet-funcs.csproj");
            var expectedCsprojContent = new[] { "Microsoft.NET.Sdk", "v4", targetFramework };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedLocalSettingsContent),
                (csprojfilepath, expectedCsprojContent)
            };

            // Initialize dotnet function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, [projectName, "--worker-runtime", "dotnet", "--target-framework", targetFramework]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_WithUnsupportedTargetFramework_FailsWithError()
        {
            string unsupportedTargetFramework = "net7.0";
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithUnsupportedTargetFramework_FailsWithError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize dotnet function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["dotnet-funcs", "--worker-runtime", "dotnet", "--target-framework", unsupportedTargetFramework], exitWith: 1);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(1);
            funcInitResult?.Should().HaveStdErrContaining($"Unable to parse target framework {unsupportedTargetFramework} for worker runtime dotnet. Valid options are net8.0, net6.0");
        }

        [Theory]
        [InlineData("")]
        [InlineData("--csx")]
        public async Task Init_WithDockerFlagAndCsx_GeneratesDockerFile(string csxParam)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDockerFlagAndCsx_GeneratesDockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet function app
            var args = new List<string> { "--worker-runtime", "dotnet", "--docker" };
            if (!string.IsNullOrEmpty(csxParam))
            {
                args.Add(csxParam);
            }

            var funcInitResult = await FuncInitWithRetryAsync(testName, args);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_WithCsxFlag_GeneratesExpectedFunctionProjectFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithCsxFlag_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedcontent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedcontent)
            };

            // Initialize dotnet function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "dotnet", "--csx"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_DockerOnlyOnExistingProject_GeneratesDockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DockerOnlyOnExistingProject_GeneratesDockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet function app with retry helper
            var initialResult = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet"]);
            Assert.NotNull(initialResult);

            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--docker-only"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async Task Init_DockerOnlyOnExistingCsxProject_GeneratesDockerfile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_DockerOnlyOnExistingCsxProject_GeneratesDockerfile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet:4" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (dockerFilePath, expectedDockerfileContent)
            };

            // Initialize dotnet function app with retry helper
            var initialResult = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet", "--csx"]);
            Assert.NotNull(initialResult);

            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--docker-only", "--csx"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(0);
            funcInitResult?.Should().WriteDockerfile();
            funcInitResult?.Should().FilesExistsWithExpectContent(filesToValidate);
        }
    }
}
