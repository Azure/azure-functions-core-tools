// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
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
            File.ReadAllText(Directory.GetFiles(workinDir, "*.csproj").Single())
                .Should().Contain("<TargetFramework>net10.0</TargetFramework>");
        }

        [Theory]
        [InlineData("net8.0")]
        [InlineData("net9.0")]
        [InlineData("net10.0")]
        [InlineData("net11.0")]
        public void Init_WithNetTargetFramework_GeneratesProjectFile_ContainsExpectedVersion(string targetFramework)
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithNetTargetFramework_GeneratesProjectFile_ContainsExpectedVersion);
            var projectName = "Test-funcs";
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, projectName, Common.Constants.LocalSettingsJsonFileName);
            var csprojfilepath = Path.Combine(workingDir, projectName, "Test-funcs.csproj");
            var expectedLocalSettingsContent = new[] { Common.Constants.FunctionsWorkerRuntime, "dotnet-isolated" };
            var expectedCsprojContent = targetFramework == "net11.0"
                ? new[] { "Azure.Functions.Sdk/1.0.1", targetFramework, "Microsoft.Azure.Functions.Worker\" Version=\"2.52.0\"", "Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore\" Version=\"2.1.1\"" }
                : new[] { "Microsoft.NET.Sdk", "v4", targetFramework };
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

            if (targetFramework == "net11.0")
            {
                File.ReadAllText(csprojfilepath).Should()
                    .NotContain("<AzureFunctionsVersion>")
                    .And.NotContain("<OutputType>")
                    .And.NotContain("Microsoft.Azure.Functions.Worker.Sdk");
            }
        }

        [Theory]
        [InlineData("fsharp", "net11.0")]
        [InlineData("F#", "NET11.0")]
        public void Init_WithNet11AndFSharp_RejectsUnsupportedLanguage(string language, string targetFramework)
        {
            var funcInitResult = new FuncInitCommand(FuncPath, nameof(Init_WithNet11AndFSharp_RejectsUnsupportedLanguage), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--worker-runtime", "dotnet-isolated", "--language", language, "--target-framework", targetFramework]);

            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining(".NET 11 isolated project initialization is not yet supported for F#");

            Directory.GetFiles(WorkingDirectory, "*.fsproj").Should().BeEmpty();
        }

        private static readonly string[] _expectedNet11ChiseledDockerfileContent =
        [
            "FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build",
            "FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated11.0-chiseled AS final",
            "COPY --from=mcr.microsoft.com/dotnet/aspnet:11.0 /usr/share/dotnet /usr/share/dotnet",
            "FUNCTIONS_WORKER_RUNTIME=dotnet-isolated",
            "FUNCTIONS_WORKER_RUNTIME_VERSION=11.0"
        ];

        [Theory]
        [InlineData("net11.0")]
        [InlineData("NET11.0")]
        public void Init_WithNet11AndDocker_GeneratesChiseledDockerfile(string targetFramework)
        {
            var funcInitResult = new FuncInitCommand(FuncPath, nameof(Init_WithNet11AndDocker_GeneratesChiseledDockerfile), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--worker-runtime", "dotnet-isolated", "--target-framework", targetFramework, "--docker"]);

            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent([(Path.Combine(WorkingDirectory, "Dockerfile"), _expectedNet11ChiseledDockerfileContent)]);
        }

        [Theory]
        [InlineData("detected", null)]
        [InlineData("explicit-lowercase", "net11.0")]
        [InlineData("explicit-uppercase", "NET11.0")]
        public async Task Init_DockerOnlyOnNet11Project_GeneratesChiseledDockerfile(string scenario, string? explicitTargetFramework)
        {
            var testName = $"{nameof(Init_DockerOnlyOnNet11Project_GeneratesChiseledDockerfile)}_{scenario}";
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated", "--target-framework", "net11.0"]);

            string[] args = explicitTargetFramework is null
                ? ["--docker-only"]
                : ["--docker-only", "--target-framework", explicitTargetFramework];

            var funcInitResult = new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(args);

            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().WriteDockerfile();
            funcInitResult.Should().FilesExistsWithExpectContent([(Path.Combine(WorkingDirectory, "Dockerfile"), _expectedNet11ChiseledDockerfileContent)]);
        }

        [Fact]
        public void Init_DotnetHelp_ListsNet11()
        {
            var funcInitResult = new FuncRootCommand(FuncPath, nameof(Init_DotnetHelp_ListsNet11), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["init", "dotnet", "--help"]);

            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining("net11.0");
        }

        [Fact]
        public void Init_WithDotnetIsolatedAndDockerFlag_GeneratesDockerFile()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDotnetIsolatedAndDockerFlag_GeneratesDockerFile);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var dockerFilePath = Path.Combine(workingDir, "Dockerfile");
            var expectedDockerfileContent = new[] { $"FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0" };
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
        public async Task Init_DockerOnlyOnExistingProjectWithTargetFramework_GeneratesDockerfile(string targetFramework)
        {
            var workingDir = Path.Combine(WorkingDirectory, targetFramework);

            try
            {
                var targetFrameworkstr = targetFramework.Replace("net", string.Empty);
                FileSystemHelpers.EnsureDirectory(workingDir);
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
            finally
            {
                Directory.Delete(workingDir, recursive: true);
            }
        }
    }
}
