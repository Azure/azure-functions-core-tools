// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using FluentAssertions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncNew
{
    [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Dotnet)]
    public class DotnetInProcTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
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

        [Fact]
        public async Task FuncNew_WithAuthLevel_Anonymous_CreatesHttpTrigger()
        {
            var methodName = nameof(FuncNew_WithAuthLevel_Anonymous_CreatesHttpTrigger);
            var uniqueTestName = methodName;

            // Initialize the function app
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet" });

            // Create func new command using consistent working directory
            var funcNewCommand = new FuncNewCommand(FuncPath, methodName, Log);
            var result = funcNewCommand
                   .WithWorkingDirectory(WorkingDirectory)
                   .Execute(new[] { ".", "--template", "HttpTrigger", "--name", "HttpAuthFunction", "--authlevel", "anonymous" });

            // Validate result
            result.Should().HaveStdOutContaining("The function \"HttpAuthFunction\" was created successfully");
        }

        [Fact]
        public async Task FuncNew_HttpTrigger_WithAuthLevelFunction_Dotnet_CreatesSuccessfully()
        {
            var uniqueTestName = nameof(FuncNew_HttpTrigger_WithAuthLevelFunction_Dotnet_CreatesSuccessfully);

            // Initialize function app
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "dotnet" });

            // Create a new HTTP Trigger function with authlevel = function
            var result = new FuncNewCommand(FuncPath, uniqueTestName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--template", "HttpTrigger", "--name", "testfunc", "--authlevel", "function"]);

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully");
        }

        [Fact]
        public void FuncNew_HttpTrigger_CsxMode_WithoutInit_Succeeds()
        {
            var testName = nameof(FuncNew_HttpTrigger_CsxMode_WithoutInit_Succeeds);

            // Create a CSX-based function without calling init
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--csx", "--template", "HttpTrigger", "--name", "testfunc"]);

            // Validate command success and output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"HttpTrigger\" template.");

            // Verify function.json contains httpTrigger
            var functionJsonPath = Path.Combine(WorkingDirectory, "testfunc", "function.json");
            var functionJson = File.ReadAllText(functionJsonPath);
            functionJson.Should().Contain("httpTrigger");
        }
    }
}
