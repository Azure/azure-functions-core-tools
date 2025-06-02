// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core
{
    public class BaseLogLevelTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        public async Task RunLogLevelOverridenViaHostJsonTest(string language, string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize function app using retry helper
            _ = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", language]);

            // Add HTTP trigger using retry helper
            _ = await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTriggerCSharp"]);

            // Modify host.json to set log level to Debug
            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)));
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["start", "--port", port.ToString()]);

            // Validate host configuration was applied
            result.Should().HaveStdOutContaining("Host configuration applied.");
        }

        public async Task RunLogLevelOverridenWithFilterTest(string language, string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize function app using retry helper
            _ = await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", language]);

            // Add HTTP trigger using retry helper
            _ = await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTriggerCSharp"]);

            // Modify host.json to set log level with filter
            string hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\", \"Host.Startup\": \"Information\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)));

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), shouldDelayForLogs: true);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--port", port.ToString()]);

            // Validate we see some logs but not others due to filters
            result.Should().HaveStdOutContaining("Found the following functions:");
            result.Should().NotHaveStdOutContaining("Reading host configuration file");
        }
    }
}
