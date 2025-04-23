// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core
{
    public class BaseMissingConfigTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        public async Task RunInvalidHostJsonTest(string language, string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", language]);

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTriggerCSharp"]);

            // Create invalid host.json
            var hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            var hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var result = new FuncStartCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)))
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--port", port.ToString()]);

            // Validate error message
            result.Should().HaveStdOutContaining("Extension bundle configuration should not be present");
        }

        public async Task RunMissingHostJsonTest(string language, string testName)
        {
            int port = ProcessHelper.GetAvailablePort();

            // Initialize function app using retry helper
            await FuncInitWithRetryAsync(testName, [".", "--worker-runtime", language]);

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, [".", "--template", "HttpTrigger", "--name", "HttpTriggerCSharp"]);

            // Delete host.json
            var hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            File.Delete(hostJsonPath);

            // Call func start
            var result = new FuncStartCommand(FuncPath, testName, Log ?? throw new ArgumentNullException(nameof(Log)))
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(["--port", port.ToString()]);

            // Validate error message
            result.Should().HaveStdOutContaining("Host.json file in missing");
        }

        public async Task RunMissingLocalSettingsJsonTest(string language, string runtimeParameter, string expectedOutput, bool invokeFunction, bool setRuntimeViaEnvironment, string testName)
        {
            var logFileName = $"{testName}_{language}_{runtimeParameter}";

            var port = ProcessHelper.GetAvailablePort();

            // Initialize function app using retry helper
            await FuncInitWithRetryAsync(logFileName, [".", "--worker-runtime", language]);

            var funcNewArgs = new[] { ".", "--template", "HttpTrigger", "--name", "HttpTriggerFunc" }
                                .Concat(!language.Contains("dotnet") ? ["--language", language] : Array.Empty<string>())
                                .ToArray();

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(logFileName, funcNewArgs);

            // Delete local.settings.json
            var localSettingsJson = Path.Combine(WorkingDirectory, "local.settings.json");
            File.Delete(localSettingsJson);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, logFileName, Log ?? throw new ArgumentNullException(nameof(Log)));

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                // Wait for host to start up if param is set, otherwise just wait 10 seconds for logs and kill the process
                if (invokeFunction)
                {
                    await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter ?? throw new ArgumentNullException(nameof(funcStartCommand.FileWriter)), "HttpTriggerFunc");
                }
                else
                {
                    await Task.Delay(10000);
                    process.Kill(true);
                }
            };

            var startCommand = new List<string> { "--port", port.ToString(), "--verbose" };
            if (!string.IsNullOrEmpty(runtimeParameter))
            {
                startCommand.Add(runtimeParameter);
            }

            // Configure the command execution
            var commandSetup = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory);

            // Conditionally set environment variable only if required
            if (setRuntimeViaEnvironment)
            {
                commandSetup = commandSetup.WithEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, language);
            }

            var result = commandSetup.Execute(["--port", port.ToString(), "--verbose"]);

            result.Should().HaveStdOutContaining(expectedOutput);
        }
    }
}
