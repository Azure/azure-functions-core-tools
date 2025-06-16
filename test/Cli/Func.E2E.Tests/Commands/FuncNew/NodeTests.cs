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
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodeTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public void FuncNew_HttpTrigger_WithAuthLevelFunction_NodeV3_CreatesSuccessfully()
        {
            var testName = nameof(FuncNew_HttpTrigger_WithAuthLevelFunction_NodeV3_CreatesSuccessfully);

            // Initialize function app with Node.js runtime and model version v3
            new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--worker-runtime", "node", "-m", "v3"]);

            // Create a new HTTP Trigger function with authlevel = function
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute([".", "--template", "HttpTrigger", "--name", "testfunc", "--authlevel", "function"]);

            // Validate expected output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully");
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task FuncNew_UsingLanguageJS_CreatesFunctionSuccessfully()
        {
            var testName = nameof(FuncNew_UsingLanguageJS_CreatesFunctionSuccessfully);
            var workingDir = WorkingDirectory;

            // Step 1: Initialize the function app with node worker and model v3
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "-m", "v3" });

            // Step 2: Run func new using alias for language and template
            var funcNewCommand = new FuncNewCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));
            var result = funcNewCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--language", "js", "--template", "HttpTrigger", "--name", "JSTestFunc"]);

            // Step 3: Validate output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"JSTestFunc\" was created successfully");
        }

        [Fact]
        public async Task FuncNew_HttpTrigger_WithNoSpaceV3TemplateName_CreatesFunctionSuccessfully()
        {
            var methodName = nameof(FuncNew_HttpTrigger_WithNoSpaceV3TemplateName_CreatesFunctionSuccessfully);
            var uniqueTestName = methodName;

            // Initialize function app with Node.js runtime and model version v3
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "node", "-m", "v3" });

            // Create a new HTTP Trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log);
            var result = funcNewCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--language", "js", "--template", "httptrigger", "--name", "testfunc" });

            // Validate expected output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }

        [Fact]
        public async Task FuncNew_HttpTrigger_WithoutV3TemplateName_CreatesFunctionSuccessfully()
        {
            var methodName = nameof(FuncNew_HttpTrigger_WithoutV3TemplateName_CreatesFunctionSuccessfully);
            var uniqueTestName = methodName;

            // Initialize function app with Node.js runtime and model version v3
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "node" });

            // Create a new HTTP Trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log);
            var result = funcNewCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--language", "js", "--template", "httptrigger", "--name", "testfunc" });

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }

        [Fact(Skip = "Flaky in model v4: validate again in stable templates")]
        public async Task FuncNew_HttpTrigger_NodeV4Model_CreatesFunctionSuccessfully()
        {
            var methodName = nameof(FuncNew_HttpTrigger_NodeV4Model_CreatesFunctionSuccessfully);
            var uniqueTestName = methodName;

            // Initialize function app with Node.js runtime and model version v4
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "node", "-m", "v4" });

            // Create a new HTTP Trigger function
            var funcNewCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log);
            var result = funcNewCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--language", "js", "--template", "httptrigger", "--name", "testfunc" });

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }

        [Fact]
        public async Task FuncNew_TypeScript_HttpTrigger_CreatesFunctionWithExpectedMetadata()
        {
            var uniqueTestName = nameof(FuncNew_TypeScript_HttpTrigger_CreatesFunctionWithExpectedMetadata);

            // Initialize the project with TypeScript and model v3
            await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "node", "--language", "typescript", "-m", "v3" });

            // Create new Http Trigger function
            var newCommand = new FuncNewCommand(FuncPath, uniqueTestName, Log);
            var result = newCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { ".", "--template", "httptrigger", "--name", "testfunc" });

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");

            var functionJsonPath = Path.Combine(WorkingDirectory, "testfunc", "function.json");
            var content = await File.ReadAllTextAsync(functionJsonPath);
            content.Should().Contain("../dist/testfunc/index.js");
            content.Should().Contain("authLevel");
            content.Should().Contain("methods");
            content.Should().Contain("httpTrigger");
        }

        [Fact]
        public void FuncNew_TypeScript_HttpTrigger_CreatesFunctionSuccessfully()
        {
            var testName = nameof(FuncNew_TypeScript_HttpTrigger_CreatesFunctionSuccessfully);
            var workingDir = WorkingDirectory;

            // Step 1: func init
            var initCommand = new FuncInitCommand(FuncPath, testName, Log);
            initCommand.WithWorkingDirectory(workingDir)
                .Execute([".", "--worker-runtime", "node", "--language", "typescript"]);

            // Step 2: func new
            var newCommand = new FuncNewCommand(FuncPath, testName, Log);
            var result = newCommand
                .WithWorkingDirectory(workingDir)
                .Execute([".", "--template", "httptrigger", "--name", "testfunc"]);

            // Step 3: Assertions
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }
    }
}
