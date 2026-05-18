// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncNew
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Go)]
    public class GoFuncNewTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void FuncNew_InGoWorkspace_FailsWithNotSupportedMessage_AndDoesNotMutateLocalSettings()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(FuncNew_InGoWorkspace_FailsWithNotSupportedMessage_AndDoesNotMutateLocalSettings);

            // Arrange: scaffold a Go workspace. local.settings.json must end up with FUNCTIONS_WORKER_RUNTIME=native.
            var initResult = new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(workingDir)
                .Execute(["--worker-runtime", "go", "--skip-go-mod-tidy"]);
            initResult.Should().ExitWith(0);

            var localSettingsPath = Path.Combine(workingDir, Common.Constants.LocalSettingsJsonFileName);
            string before = File.ReadAllText(localSettingsPath);
            before.Should().Contain("\"native\"", "init writes FUNCTIONS_WORKER_RUNTIME=native for Go projects");

            // Act: 'func new' in a Go workspace previously hung at the language/template prompt and could
            // overwrite FUNCTIONS_WORKER_RUNTIME from "native" to "go". It should now bail with a clear message.
            var newResult = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "HttpTrigger", "--name", "MyFunc"]);

            // Assert: friendly error surfaces and local.settings.json is untouched.
            newResult.Should().HaveStdErrContaining("The 'func new' command is not yet supported for the Go runtime.");
            string after = File.ReadAllText(localSettingsPath);
            after.Should().Be(before, "func new must not mutate local.settings.json for unsupported runtimes");
        }

        [Fact]
        public void FuncNew_WithWorkerRuntimeGoFlag_InEmptyDir_FailsWithNotSupportedMessage()
        {
            var workingDir = WorkingDirectory;
            var testName = nameof(FuncNew_WithWorkerRuntimeGoFlag_InEmptyDir_FailsWithNotSupportedMessage);

            // 'func new --worker-runtime go' in an empty dir would normally trigger an implicit
            // 'func init' before scaffolding the function. The Go guard should fire after init resolves
            // the runtime to Go, before any function-template work runs.
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "go", "--template", "HttpTrigger", "--name", "MyFunc", "--skip-go-mod-tidy"]);

            result.Should().HaveStdErrContaining("The 'func new' command is not yet supported for the Go runtime.");
            File.Exists(Path.Combine(workingDir, "MyFunc")).Should().BeFalse("no function directory should be created");
        }
    }
}
