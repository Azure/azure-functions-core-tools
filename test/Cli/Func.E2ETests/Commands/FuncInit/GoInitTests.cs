// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncInit
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Go)]
    public class GoInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Init_WithGo_GeneratesExpectedFunctionProjectFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithGo_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            var expectedLocalSettingsContent = new[] { Common.Constants.FunctionsWorkerRuntime, "native" };
            var hostJsonPath = Path.Combine(workingDir, "host.json");
            var expectedHostJsonContent = new[] { "extensionBundle" };
            var mainGoPath = Path.Combine(workingDir, "main.go");
            var expectedMainGoContent = new[] { "package main" };
            var funcIgnorePath = Path.Combine(workingDir, ".funcignore");

            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (localSettingsPath, expectedLocalSettingsContent),
                (hostJsonPath, expectedHostJsonContent),
                (mainGoPath, expectedMainGoContent)
            };

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "go"]);

            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult.Should().FilesExistsWithExpectContent(filesToValidate);
            File.Exists(funcIgnorePath).Should().BeTrue("Expected .funcignore to exist");
            File.Exists(Path.Combine(workingDir, "go.mod")).Should().BeTrue("Expected go.mod to exist");
        }

        [Fact]
        public void Init_WithSkipGoModTidy_SkipsTidyStep()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithSkipGoModTidy_SkipsTidyStep);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "go", "--skip-go-mod-tidy"]);

            funcInitResult.Should().ExitWith(0);
            funcInitResult.Should().HaveStdOutContaining("Skipped \"go mod tidy\"");
        }

        [Fact]
        public void Init_WithForceFlag_InNonEmptyDirectory()
        {
            var workingDir = WorkingDirectory;
            File.WriteAllText(Path.Combine(workingDir, "existing.txt"), "test");

            var testName = nameof(Init_WithForceFlag_InNonEmptyDirectory);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "go", "--force"]);

            funcInitResult.Should().ExitWith(0);
            File.Exists(Path.Combine(workingDir, "main.go")).Should().BeTrue("Expected main.go to exist");
        }

        [Fact]
        public void Init_WithDockerFlag_FailsLoudly()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDockerFlag_FailsLoudly);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "go", "--docker"]);

            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining("Docker support for the Go worker runtime is not yet available");
            File.Exists(Path.Combine(workingDir, "Dockerfile")).Should().BeFalse("Expected no Dockerfile for Go runtime");
        }

        [Fact]
        public void Init_WithGolangAlias_ResolvesToGo()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithGolangAlias_ResolvesToGo);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "golang"]);

            funcInitResult.Should().ExitWith(0);
            File.Exists(Path.Combine(workingDir, "main.go")).Should().BeTrue("Expected main.go to exist");
            File.Exists(Path.Combine(workingDir, "go.mod")).Should().BeTrue("Expected go.mod to exist");
        }

        [SkipIfGoNonExistFact]
        public async Task Init_WithGo_ScaffoldedProjectCompiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithGo_ScaffoldedProjectCompiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "go"]);

            funcInitResult.Should().ExitWith(0);

            var goBuild = new Executable("go", "build ./...", workingDirectory: workingDir);
            var exitCode = await goBuild.RunAsync(
                l =>
                {
                    if (!string.IsNullOrEmpty(l))
                    {
                        Log!.WriteLine(l);
                    }
                },
                e =>
                {
                    if (!string.IsNullOrEmpty(e))
                    {
                        Log!.WriteLine(e);
                    }
                });
            exitCode.Should().Be(0, "scaffolded Go project should compile with 'go build ./...'");
        }
    }

    public sealed class SkipIfGoNonExistFact : FactAttribute
    {
        public SkipIfGoNonExistFact()
        {
            var goExe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "go.exe" : "go";
            if (!CheckIfGoExists(goExe))
            {
                Skip = "go does not exist on PATH";
            }
        }

        private static bool CheckIfGoExists(string goExe)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            foreach (var p in path.Split(Path.PathSeparator))
            {
                if (File.Exists(Path.Combine(p, goExe)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
