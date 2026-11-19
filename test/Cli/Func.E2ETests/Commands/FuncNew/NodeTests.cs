// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using AwesomeAssertions;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncNew
{
    [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodeTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("javascript", "Office 365 trigger", "office365NewEmailPoll", "js")]
        [InlineData("typescript", "Office 365 trigger", "office365NewEmailPoll", "ts")]
        [InlineData("javascript", "SharePoint trigger", "sharepointNewFilePoll", "js")]
        [InlineData("typescript", "SharePoint trigger", "sharepointNewFilePoll", "ts")]
        [InlineData("javascript", "OneDrive trigger", "oneDriveNewFilePoll", "js")]
        [InlineData("typescript", "OneDrive trigger", "oneDriveNewFilePoll", "ts")]
        [InlineData("javascript", "Dataverse trigger", "dataverseNewRowPoll", "js")]
        [InlineData("typescript", "Dataverse trigger", "dataverseNewRowPoll", "ts")]
        [InlineData("javascript", "Teams trigger", "teamsNewChannelMessagePoll", "js")]
        [InlineData("typescript", "Teams trigger", "teamsNewChannelMessagePoll", "ts")]
        public void FuncNew_NodeV4Connector_UsesEmbeddedTemplateWithoutBundle(
            string language, string templateName, string defaultName, string extension)
        {
            var testName = $"{nameof(FuncNew_NodeV4Connector_UsesEmbeddedTemplateWithoutBundle)}_{defaultName}_{language}";
            var init = new FuncInitCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE", "true")
                .Execute([".", "--worker-runtime", "node", "--language", language, "--model", "V4", "--no-bundle", "--skip-npm-install"]);
            init.Should().ExitWith(0);

            foreach (var functionName in new[] { defaultName, $"custom{defaultName}" })
            {
                var result = new FuncNewCommand(FuncPath, testName, Log)
                    .WithWorkingDirectory(WorkingDirectory)
                    .WithEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE", "true")
                    .Execute(["--language", language, "--template", $"\"{templateName}\"", "--name", functionName]);

                result.Should().ExitWith(0);
                result.Should().HaveStdOutContaining($"The function \"{functionName}\" was created successfully");
                var code = File.ReadAllText(Path.Combine(WorkingDirectory, "src", "functions", $"{functionName}.{extension}"));
                code.Should().Contain($"'{functionName}'");
                code.Should().Contain("deliveryMode: DeliveryMode.Poll");
                code.Should().NotContain("%functionName%");
                code.Should().NotContain("cardinality:");
                code.Should().NotContain("maxBatchSize:");
            }

            Directory.GetFiles(WorkingDirectory, "function.json", SearchOption.AllDirectories).Should().BeEmpty();
        }

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

        [Fact]
        public async Task FuncNew_JavaScript_DurableOrchestrator_AddsDurableFunctionsDependency()
        {
            var testName = nameof(FuncNew_JavaScript_DurableOrchestrator_AddsDurableFunctionsDependency);

            // Initialize a Node.js v4 (default) JavaScript function app
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "--language", "javascript" });

            // Create a Durable Functions orchestrator. Run in offline mode so the assertion
            // validates the package.json update without requiring a real 'npm install'.
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE", "true")
                .Execute([".", "--template", "DurableFunctionsOrchestrator", "--name", "durableHello"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"durableHello\" was created successfully");
            result.Should().HaveStdOutContaining("Added 'durable-functions' to package.json.");

            var packageJson = await File.ReadAllTextAsync(Path.Combine(WorkingDirectory, "package.json"));
            packageJson.Should().Contain("durable-functions");
        }

        [Fact]
        public async Task FuncNew_TypeScript_DurableEntity_AddsDurableFunctionsDependency()
        {
            var testName = nameof(FuncNew_TypeScript_DurableEntity_AddsDurableFunctionsDependency);

            // Initialize a Node.js v4 (default) TypeScript function app
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "--language", "typescript" });

            // Create a Durable Functions entity. Run in offline mode so the assertion
            // validates the package.json update without requiring a real 'npm install'.
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE", "true")
                .Execute([".", "--template", "DurableFunctionsEntity", "--name", "counter"]);

            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"counter\" was created successfully");
            result.Should().HaveStdOutContaining("Added 'durable-functions' to package.json.");

            var packageJson = await File.ReadAllTextAsync(Path.Combine(WorkingDirectory, "package.json"));
            packageJson.Should().Contain("durable-functions");
        }

        [Fact]
        public async Task FuncNew_JavaScript_HttpTrigger_DoesNotAddDurableFunctionsDependency()
        {
            var testName = nameof(FuncNew_JavaScript_HttpTrigger_DoesNotAddDurableFunctionsDependency);

            // Initialize a Node.js v4 (default) JavaScript function app
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "--language", "javascript" });

            // A non-durable template must not pull in the durable-functions dependency
            var result = new FuncNewCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable("FUNCTIONS_CORE_TOOLS_OFFLINE", "true")
                .Execute([".", "--template", "HttpTrigger", "--name", "myHttp"]);

            result.Should().ExitWith(0);
            result.Should().NotHaveStdOutContaining("Added 'durable-functions' to package.json.");

            var packageJson = await File.ReadAllTextAsync(Path.Combine(WorkingDirectory, "package.json"));
            packageJson.Should().NotContain("durable-functions");
        }
    }
}
