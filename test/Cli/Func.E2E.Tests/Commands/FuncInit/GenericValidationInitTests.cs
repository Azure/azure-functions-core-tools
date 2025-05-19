// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncInit
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
        public void Init_WithWorkerRuntimeTypescript_SuccessfulExecution()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithWorkerRuntimeTypescript_SuccessfulExecution);
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
