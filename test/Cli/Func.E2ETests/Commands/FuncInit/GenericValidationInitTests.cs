// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncInit
{
    /// <summary>
    /// E2E tests for func init validation scenarios.
    /// NOTE: Pure validation logic tests have been moved to unit tests in Func.UnitTests:
    /// - WorkerRuntimeLanguageHelperTests covers worker runtime validation
    /// - TargetFrameworkHelperTests covers target framework validation
    /// These E2E tests remain to verify CLI integration and error message output.
    /// </summary>
    public class GenericValidationInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        /// <summary>
        /// E2E test to verify CLI outputs correct error message for unknown worker runtime.
        /// Validation logic is unit tested in WorkerRuntimeLanguageHelperTests.NormalizeWorkerRuntime_WithInvalidInputs_ThrowsArgumentException
        /// </summary>
        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Custom)]
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
