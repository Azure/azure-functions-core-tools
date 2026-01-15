// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2ETests.Fixtures;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2ETests.Commands.FuncStart.Core
{
    /// <summary>
    /// Helper methods for log level tests. Provides shared test methods that can work with
    /// either pre-built fixtures or dynamically created apps.
    /// </summary>
    public static class LogLevelTestHelpers
    {
        /// <summary>
        /// Runs a log level override test using host.json configuration.
        /// </summary>
        public static Task RunLogLevelOverridenViaHostJsonTest(
            string funcPath,
            string workingDirectory,
            ITestOutputHelper log,
            string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Modify host.json to set log level to Debug
            string hostJsonPath = Path.Combine(workingDirectory, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"Debug\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            var funcStartCommand = new FuncStartCommand(funcPath, testName, log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)));
            };

            var result = funcStartCommand
                .WithWorkingDirectory(workingDirectory)
                .Execute(["start", "--port", port.ToString()]);

            // Validate host configuration was applied
            result.Should().HaveStdOutContaining("Host configuration applied.");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Runs a log level override test using filtered log levels in host.json.
        /// </summary>
        public static Task RunLogLevelOverridenWithFilterTest(
            string funcPath,
            string workingDirectory,
            ITestOutputHelper log,
            string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Modify host.json to set log level with filter
            string hostJsonPath = Path.Combine(workingDirectory, "host.json");
            string hostJsonContent = "{\"version\": \"2.0\",\"logging\": {\"logLevel\": {\"Default\": \"None\", \"Host.Startup\": \"Information\"}}}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(funcPath, testName, log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), shouldDelayForLogs: true);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(workingDirectory)
                .Execute(["--port", port.ToString()]);

            // Validate we see some logs but not others due to filters
            result.Should().HaveStdOutContaining("Found the following functions:");
            result.Should().NotHaveStdOutContaining("Reading host configuration file");

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Legacy base class for log level tests that create apps dynamically.
    /// Prefer using LogLevelTestHelpers with fixtures for new tests.
    /// </summary>
    public class BaseLogLevelTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        public async Task RunLogLevelOverridenViaHostJsonTest(string language, string testName)
        {
            // Initialize function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", language]);

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTriggerCSharp"]);

            await LogLevelTestHelpers.RunLogLevelOverridenViaHostJsonTest(FuncPath, WorkingDirectory, Log, testName);
        }

        public async Task RunLogLevelOverridenWithFilterTest(string language, string testName)
        {
            // Initialize function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", language]);

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTriggerCSharp"]);

            await LogLevelTestHelpers.RunLogLevelOverridenWithFilterTest(FuncPath, WorkingDirectory, Log, testName);
        }
    }
}
