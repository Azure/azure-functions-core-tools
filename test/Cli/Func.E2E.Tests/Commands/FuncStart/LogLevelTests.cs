// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class LogLevelTests(ITestOutputHelper log) : BaseLogLevelTests(log)
    {
        [Fact]
        public async Task Start_Dotnet_Isolated_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            await RunLogLevelOverridenViaHostJsonTest("dotnet-isolated", nameof(Start_Dotnet_Isolated_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue));
        }

        [Fact]
        public async Task Start_Dotnet_Isolated_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue()
        {
            await RunLogLevelOverridenWithFilterTest("dotnet-isolated", nameof(Start_Dotnet_Isolated_LogLevelOverridenWithFilter_LogLevelSetToExpectedValue));
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_LanguageWorker_LogLevelOverridenViaSettings_LogLevelSetToExpectedValue);

            // Initialize Node.js function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "-m", "v4"]);

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger", "--language", "node"], workerRuntime: "node");

            // Add debug log level setting
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                    .WithWorkingDirectory(WorkingDirectory)
                                    .Execute(["add", "AzureFunctionsJobHost__logging__logLevel__Default", "Debug"]);
            funcSettingsResult.Should().ExitWith(0);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };
            var result = funcStartCommand
                        .WithWorkingDirectory(WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                        .Execute(["--port", port.ToString(), "--verbose"]);

            // Validate we see detailed worker logs
            result.Should().HaveStdOutContaining("Workers Directory set to");
        }

        [Fact]
        public async Task Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_LanguageWorker_LogLevelOverridenViaHostJson_LogLevelSetToExpectedValue);

            // Initialize Node.js function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", "node", "-m", "v4"]);

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTrigger", "--language", "node"], workerRuntime: "node");

            // Modify host.json to set log level
            var hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            var hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);
            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTrigger?name=Test");
            };
            var result = funcStartCommand
                        .WithWorkingDirectory(WorkingDirectory)
                        .WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, "node")
                        .Execute(["--port", port.ToString()]);

            // Validate minimal worker logs due to "None" log level
            result.Should().HaveStdOutContaining("Worker process started and initialized");
            result.Should().NotHaveStdOutContaining("Initializing function HTTP routes");
        }
    }
}
