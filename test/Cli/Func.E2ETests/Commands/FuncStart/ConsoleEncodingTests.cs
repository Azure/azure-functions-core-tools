// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.E2ETests.Traits;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart
{
    public class ConsoleEncodingTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Node)]
        public async Task Start_WithNode_WithNonAsciiLogging_DisplaysCorrectly()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_WithNode_WithNonAsciiLogging_DisplaysCorrectly);
            var japaneseText = "こんにちは";

            // Initialize Node.js function app
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "-m", "v4"]);

            // Add HTTP trigger
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger", "--language", "node"], workerRuntime: "node");

            // Modify the function to log non-ASCII text
            string jsFilePath = Path.Combine(WorkingDirectory, "src", "functions", "HttpTrigger.js");
            string originalContent = File.ReadAllText(jsFilePath);

            // Find the handler function's opening '{'
            var handlerSignature = "handler: async (request, context) => {";
            int handlerIndex = originalContent.IndexOf(handlerSignature, StringComparison.Ordinal);
            if (handlerIndex >= 0)
            {
                int bodyStart = originalContent.IndexOf('{', handlerIndex);
                if (bodyStart >= 0)
                {
                    bodyStart++; // Move past the '{'
                    string logStatement = $"\n        context.log(\"Test String: {japaneseText}\");";
                    string modifiedContent = originalContent.Insert(bodyStart, logStatement);
                    File.WriteAllText(jsFilePath, modifiedContent);
                }
            }

            // Execute the function
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                .Execute(["--port", port.ToString()]);

            // Verify the Japanese text was correctly displayed (not as question marks)
            result.Should().HaveStdOutContaining($"Test String: {japaneseText}");
            result.Should().NotHaveStdOutContaining("Test String: ?????");
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.DotnetIsolated)]
        public async Task Start_WithDotnetIsolated_WithNonAsciiLogging_DisplaysCorrectly()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_WithDotnetIsolated_WithNonAsciiLogging_DisplaysCorrectly);
            var japaneseText = "こんにちは";

            // Initialize .NET function app
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "dotnet-isolated"]);

            // Add HTTP trigger
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger"]);

            // Modify the function to log non-ASCII text
            var csFilesPath = Path.Combine(WorkingDirectory, "HttpTrigger.cs");
            string originalContent = File.ReadAllText(csFilesPath);

            // Find the Run method signature
            var methodSignature = "IActionResult Run(";
            int methodIndex = originalContent.IndexOf(methodSignature, StringComparison.Ordinal);
            if (methodIndex >= 0)
            {
                // Find the first '{' after the method signature
                int bodyStart = originalContent.IndexOf('{', methodIndex);
                if (bodyStart >= 0)
                {
                    bodyStart++; // Move past the '{'
                    string logStatement = $"\n        _logger.LogInformation(\"Test String: {japaneseText}\");";
                    string modifiedContent = originalContent.Insert(bodyStart, logStatement);
                    File.WriteAllText(csFilesPath, modifiedContent);
                }
            }

            // Execute the function
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--port", port.ToString()]);

            // Verify the Japanese text was correctly displayed (not as question marks)
            result.Should().HaveStdOutContaining($"Test String: {japaneseText}");
            result.Should().NotHaveStdOutContaining("Test String: ?????");
        }

        [Fact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Powershell)]
        public async Task Start_WithPowerShell_WithNonAsciiLogging_DisplaysCorrectly()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_WithPowerShell_WithNonAsciiLogging_DisplaysCorrectly);
            var japaneseText = "こんにちは";

            // Initialize PowerShell function app
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "powershell"]);

            // Add HTTP trigger
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger", "--language", "powershell"], workerRuntime: "powershell");

            // Modify the function to log non-ASCII text
            string ps1FilePath = Path.Combine(WorkingDirectory, "HttpTrigger", "run.ps1");
            string originalContent = File.ReadAllText(ps1FilePath);

            // Replace the existing log statement with both original and test log
            string oldLogStatement = "Write-Host \"PowerShell HTTP trigger function processed a request.\"";
            string newLogStatement = $"Write-Host \"PowerShell HTTP trigger function processed a request.\"\nWrite-Host \"Test String: {japaneseText}\"";
            string modifiedContent = originalContent.Replace(oldLogStatement, newLogStatement);
            File.WriteAllText(ps1FilePath, modifiedContent);

            // Execute the function
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "powershell")
                .Execute(["--port", port.ToString()]);

            // Verify the Japanese text was correctly displayed (not as question marks)
            result.Should().HaveStdOutContaining($"Test String: {japaneseText}");
            result.Should().NotHaveStdOutContaining("Test String: ?????");
        }
    }
}
