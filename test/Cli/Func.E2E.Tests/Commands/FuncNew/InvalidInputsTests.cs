// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    public class InvalidInputsTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public void FuncNew_TimerTrigger_WithAuthLevelFunction_Node_InvalidAuthLevel()
        {
            var testName = nameof(FuncNew_TimerTrigger_WithAuthLevelFunction_Node_InvalidAuthLevel);

            // Initialize function app with Node.js runtime and model version v3
            new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--worker-runtime", "node", "-m", "v3"]);

            // Create a new Timer Trigger function with authlevel = function
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--template", "TimerTrigger", "--name", "TimerTrigFunc", "--authlevel", "function"]);

            // Validate expected output
            result.Should().ExitWith(1);
            result.Should().HaveStdErrContaining("Unable to configure Authorization level");
        }

        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task FuncNew_HttpTrigger_InvalidAuthLevel_ShowsError()
        {
            var uniqueTestName = nameof(FuncNew_HttpTrigger_InvalidAuthLevel_ShowsError);

            // Initialize function app with node runtime and v3 model
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "node", "-m", "v3" });

            // Create function with invalid auth level
            var result = new FuncNewCommand(FuncPath, uniqueTestName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--template", "httpTrigger", "--name", "testfunc", "--authlevel", "invalid"]);

            // Validate error message
            var functionPath = Path.Combine(WorkingDirectory, uniqueTestName, "testfunc");
            Directory.Exists(functionPath).Should().BeFalse("Function directory should not be created with invalid authlevel.");
        }

        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
        public async Task FuncNew_InvalidTemplate_ShowsError()
        {
            var methodName = nameof(FuncNew_InvalidTemplate_ShowsError);
            var uniqueTestName = methodName;

            // Initialize the function app
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet" });

            // Create func new command using consistent working directory
            var funcNewCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log);

            var result = funcNewCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "InvalidTrigger", "--name", "InvalidFunction" });

            // Validate result
            result.Should().HaveStdErrContaining("Unknown template 'InvalidTrigger'");
        }

        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
        public async Task FuncNew_WithoutName_ShowsError()
        {
            var methodName = nameof(FuncNew_WithoutName_ShowsError);
            var uniqueTestName = methodName;

            // Initialize the function app
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet" });

            // Create func new command using consistent working directory
            var funcNewCommand = new FuncNewCommand(FuncPath, methodName, Log);
            var result = funcNewCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "HttpTrigger" });

            // Validate result
            result.Should().HaveStdErrContaining("Command must specify --template, and --name explicitly");
        }
    }
}
