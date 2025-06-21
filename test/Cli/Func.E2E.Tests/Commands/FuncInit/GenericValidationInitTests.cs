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
    public class GenericValidationInitTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public async Task Init_WithUnknownWorkerRuntime_DisplayNotValidOptionError()
        {
            const string unknownWorkerRuntime = "foo";
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithUnknownWorkerRuntime_DisplayNotValidOptionError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize function app with unsupported worker runtime
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", unknownWorkerRuntime], exitWith: 1);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(1);
            funcInitResult?.Should().HaveStdErrContaining($"Worker runtime '{unknownWorkerRuntime}' is not a valid option.");
        }

        [Fact]
        public async Task Init_WithDockerOnlyWithoutProject_ExpectedToFailWithError()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithDockerOnlyWithoutProject_ExpectedToFailWithError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize function app with docker-only flag without existing project
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--docker-only"], exitWith: 1);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(1);
            funcInitResult?.Should().HaveStdErrContaining($"Can't determine project language from files.");
        }

        [Fact]
        public async Task Init_WithManagedDependenciesOnUnsupportedWorkerRuntime_FailsWithError()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithManagedDependenciesOnUnsupportedWorkerRuntime_FailsWithError);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize function app with managed dependencies on unsupported runtime
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "python", "--managed-dependencies"], exitWith: 1);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().ExitWith(1);
            funcInitResult?.Should().HaveStdErrContaining($"Managed dependencies is only supported for PowerShell.");
        }

        [Fact]
        public async Task Init_WithWorkerRuntimeTypescript_GeneratesExpectedFunctionProjectFiles()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(Init_WithWorkerRuntimeTypescript_GeneratesExpectedFunctionProjectFiles);
            var funcInitCommand = new FuncInitCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            // Initialize typescript function app
            var funcInitResult = await FuncInitWithRetryAsync(testName, ["--worker-runtime", "typescript"]);

            // Validate expected output content
            Assert.NotNull(funcInitResult);
            funcInitResult?.Should().WriteVsCodeExtensionsJsonAndExitWithZero(workingDir);
            funcInitResult?.Should().HaveStdOutContaining($"Writing tsconfig.json");
        }
    }
}
