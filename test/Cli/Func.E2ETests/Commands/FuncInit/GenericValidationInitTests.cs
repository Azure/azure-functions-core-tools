// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncInit
{
    public class GenericValidationInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void Init_WithUnknownWorkerRuntime_DisplayNotValidOptionError()
        {
            const string unknownWorkerRuntime = "foo";
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithUnknownWorkerRuntime_DisplayNotValidOptionError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize function app with unsupported worker runtime
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", unknownWorkerRuntime]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"Worker runtime '{unknownWorkerRuntime}' is not a valid option.");
        }

        [Fact]
        public void Init_WithDockerOnlyWithoutProject_ExpectedToFailWithError()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDockerOnlyWithoutProject_ExpectedToFailWithError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize function app with unsupported worker runtime
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--docker-only"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"Can't determine project language from files.");
        }

        [Fact]
        public void Init_WithFlexByocWithoutDockerMode_ExpectedToFailWithError()
        {
            var funcInitResult = new FuncInitCommand(FuncPath, nameof(Init_WithFlexByocWithoutDockerMode_ExpectedToFailWithError), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--flex-byoc"]);

            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining("The --flex-byoc option requires either --docker or --docker-only.");
        }

        [Fact]
        public void Init_WithFlexByocAndConflictingDockerModes_ExpectedToFailWithError()
        {
            var funcInitResult = new FuncInitCommand(FuncPath, nameof(Init_WithFlexByocAndConflictingDockerModes_ExpectedToFailWithError), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--docker", "--docker-only", "--flex-byoc"]);

            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining("The --docker and --docker-only options cannot be used together with --flex-byoc.");
        }

        [Fact]
        public void Init_WithFlexByocWithoutWorkerRuntime_ExpectedToFailWithError()
        {
            var funcInitResult = new FuncInitCommand(FuncPath, nameof(Init_WithFlexByocWithoutWorkerRuntime_ExpectedToFailWithError), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--docker-only", "--flex-byoc"]);

            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining("The --flex-byoc option requires --worker-runtime.");
        }

        [Fact]
        public void Init_WithRuntimeVersionWithoutFlexByoc_ExpectedToFailWithError()
        {
            var funcInitResult = new FuncInitCommand(FuncPath, nameof(Init_WithRuntimeVersionWithoutFlexByoc_ExpectedToFailWithError), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--docker-only", "--worker-runtime", "powershell", "--runtime-version", "7.4"]);

            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining("The --runtime-version option is supported only with --flex-byoc.");
        }

        [Fact]
        public void Init_WithFlexByoc_GeneratesContainerArtifacts()
        {
            var funcInitResult = new FuncInitCommand(FuncPath, nameof(Init_WithFlexByoc_GeneratesContainerArtifacts), Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--docker-only", "--flex-byoc", "--worker-runtime", "powershell"]);

            funcInitResult.Should().ExitWith(0);
            Assert.True(File.Exists(Path.Combine(WorkingDirectory, "Dockerfile")));
            Assert.True(File.Exists(Path.Combine(WorkingDirectory, "entrypoint.sh")));
            Assert.True(File.Exists(Path.Combine(WorkingDirectory, ".dockerignore")));
        }

        [Fact]
        public void Init_WithManagedDependenciesOnUnsupportedWorkerRuntime_FailsWithError()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithManagedDependenciesOnUnsupportedWorkerRuntime_FailsWithError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize function app
            var funcInitResult = funcInitCommand
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "python", "--managed-dependencies"]);

            // Validate expected output content
            funcInitResult.Should().ExitWith(1);
            funcInitResult.Should().HaveStdErrContaining($"Managed dependencies is only supported for PowerShell.");
        }

        [Fact]
        public void Init_WithWorkerRuntimeTypescript_GeneratesExpectedFunctionProjectFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithWorkerRuntimeTypescript_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize dotnet function app
            var funcInitResult = funcInitCommand
               .WithWorkingDirectory(workingDir)
               .Execute(["--worker-runtime", "typescript"]);

            // Validate expected output content
            funcInitResult.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult.Should().HaveStdOutContaining($"Writing tsconfig.json");
        }
    }
}
