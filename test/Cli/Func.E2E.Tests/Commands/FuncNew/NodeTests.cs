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
    [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
    public class NodeTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        public async void FuncNew_HttpTrigger_WithAuthLevelFunction_NodeV3_CreatesSuccessfully()
        {
            var testName = nameof(FuncNew_HttpTrigger_WithAuthLevelFunction_NodeV3_CreatesSuccessfully);

            // Initialize function app with Node.js runtime and model version v3
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "-m", "v3" });

            // Run func new
            var args = new[] { ".", "--template", "HttpTrigger", "--name", "testfunc", "--authlevel", "function" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "node");

            // Validate expected output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully");
        }

        [Fact]
        [Trait(TestTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task FuncNew_UsingLanguageJS_CreatesFunctionSuccessfully()
        {
            var testName = nameof(FuncNew_UsingLanguageJS_CreatesFunctionSuccessfully);
            var workingDir = WorkingDirectory;

            // Initialize the function app with node worker and model v3
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "-m", "v3" });

            // Run func new
            var args = new[] { ".", "--language", "js", "--template", "HttpTrigger", "--name", "JSTestFunc" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "node");

            // Step 3: Validate output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"JSTestFunc\" was created successfully");
        }

        [Fact]
        public async Task FuncNew_HttpTrigger_WithNoSpaceV3TemplateName_CreatesFunctionSuccessfully()
        {
            var testName = nameof(FuncNew_HttpTrigger_WithNoSpaceV3TemplateName_CreatesFunctionSuccessfully);

            // Initialize function app with Node.js runtime and model version v3
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "-m", "v3" });

            // Run func new
            var args = new[] { ".", "--language", "js", "--template", "httptrigger", "--name", "testfunc" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "node");

            // Validate expected output
            result.Should().ExitWith(0);
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }

        [Fact]
        public async Task FuncNew_HttpTrigger_WithoutV3TemplateName_CreatesFunctionSuccessfully()
        {
            var testName = nameof(FuncNew_HttpTrigger_WithoutV3TemplateName_CreatesFunctionSuccessfully);

            // Initialize function app with Node.js runtime and model version v3
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node" });

            // Run func new
            var args = new[] { ".", "--language", "js", "--template", "httptrigger", "--name", "testfunc" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "node");

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }

        [Fact(Skip = "Flaky in model v4: validate again in stable templates")]
        public async Task FuncNew_HttpTrigger_NodeV4Model_CreatesFunctionSuccessfully()
        {
            var testName = nameof(FuncNew_HttpTrigger_NodeV4Model_CreatesFunctionSuccessfully);

            // Initialize function app with Node.js runtime and model version v4
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "-m", "v4" });

            // Run func new
            var args = new[] { ".", "--language", "js", "--template", "httptrigger", "--name", "testfunc" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "node");

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }

        [Fact]
        public async Task FuncNew_TypeScript_HttpTrigger_CreatesFunctionWithExpectedMetadata()
        {
            var testName = nameof(FuncNew_TypeScript_HttpTrigger_CreatesFunctionWithExpectedMetadata);
            var functionJsonPath = Path.Combine(WorkingDirectory, "testfunc", "function.json");
            var expectedcontent = new[] { "../dist/testfunc/index.js", "authLevel", "methods", "httpTrigger" };
            var filesToValidate = new List<(string FilePath, string[] ExpectedContent)>
            {
                (functionJsonPath, expectedcontent)
            };

            // Initialize the project with TypeScript and model v3
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "--language", "typescript", "-m", "v3" });

            // Run func new
            var args = new[] { ".", "--template", "httptrigger", "--name", "testfunc" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "node");

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
            result.Should().FilesExistsWithExpectContent(filesToValidate);
        }

        [Fact]
        public async void FuncNew_TypeScript_HttpTrigger_CreatesFunctionSuccessfully()
        {
            var testName = nameof(FuncNew_TypeScript_HttpTrigger_CreatesFunctionSuccessfully);
            var workingDir = WorkingDirectory;

            // func init
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "--language", "typescript" });

            // Run func new
            var args = new[] { ".", "--template", "httptrigger", "--name", "testfunc" };
            var result = await FuncNewWithResultRetryAsync(testName, args, "node");

            // Validate expected output
            result.Should().HaveStdOutContaining("The function \"testfunc\" was created successfully from the \"httptrigger\" template.");
        }
    }
}
