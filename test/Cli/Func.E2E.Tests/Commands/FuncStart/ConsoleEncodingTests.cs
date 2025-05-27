// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class ConsoleEncodingTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task Start_NodeFunction_WithNonAsciiLogging_DisplaysCorrectly()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_NodeFunction_WithNonAsciiLogging_DisplaysCorrectly);
            var japaneseText = "こんにちは";

            // Initialize Node.js function app
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "-m", "v4"]);

            // Add HTTP trigger
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger", "--language", "node"], workerRuntime: "node");

            // Modify the function to log non-ASCII text
            string indexPath = Path.Combine(WorkingDirectory, "HttpTrigger", "index.js");
            string originalContent = File.ReadAllText(indexPath);
            string modifiedContent = originalContent.Replace(
                "module.exports = async function (context, req) {",
                $"module.exports = async function (context, req) {{\n    context.log(\"Test String: {japaneseText}\");"
            );
            File.WriteAllText(indexPath, modifiedContent);

            // Execute the function
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };
            
            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                .Execute(["--port", port.ToString(), "--verbose"]);

            // Verify the Japanese text was correctly displayed (not as question marks)
            result.Should().HaveStdOutContaining($"Test String: {japaneseText}");
            result.Should().NotHaveStdOutContaining("Test String: ?????");
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        public async Task Start_DotNetFunction_WithNonAsciiLogging_DisplaysCorrectly()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_DotNetFunction_WithNonAsciiLogging_DisplaysCorrectly);
            var japaneseText = "こんにちは";

            // Initialize .NET function app
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated"]);

            // Add HTTP trigger
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger"]);

            // Modify the function to log non-ASCII text
            var csFiles = Directory.GetFiles(Path.Combine(WorkingDirectory, "HttpTrigger"), "*.cs");
            if (csFiles.Length > 0)
            {
                string codeFile = csFiles[0];
                string originalContent = File.ReadAllText(codeFile);
                string modifiedContent = originalContent.Replace(
                    "public async Task<IActionResult> Run(",
                    $"public async Task<IActionResult> Run(\n            [HttpTrigger(AuthorizationLevel.Function, \"get\", \"post\", Route = null)] HttpRequest req,\n            ILogger log)\n        {{\n            log.LogInformation(\"Test String: {japaneseText}\");"
                );
                
                // Remove the original parameter declaration
                modifiedContent = modifiedContent.Replace(
                    "[HttpTrigger(AuthorizationLevel.Function, \"get\", \"post\", Route = null)] HttpRequest req,\n            ILogger log)", 
                    ""
                );
                
                File.WriteAllText(codeFile, modifiedContent);
            }

            // Execute the function
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };
            
            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--port", port.ToString(), "--verbose"]);

            // Verify the Japanese text was correctly displayed (not as question marks)
            result.Should().HaveStdOutContaining($"Test String: {japaneseText}");
            result.Should().NotHaveStdOutContaining("Test String: ?????");
        }
    }
}