// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Func.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class MissingConfigTests(ITestOutputHelper log) : BaseMissingConfigTests(log)
    {
        [Fact(Skip="Test fails and needs to be investiagted on why it does.")]
        public async Task Start_Dotnet_Isolated_InvalidHostJson_FailsWithExpectedError()
        {
            await RunInvalidHostJsonTest("dotnet-isolated", nameof(Start_Dotnet_Isolated_InvalidHostJson_FailsWithExpectedError));
        }

        [Fact(Skip = "Test fails and needs to be investiagted on why it does.")]
        public async Task Start_Dotnet_Isolated_MissingHostJson_FailsWithExpectedError()
        {
            await RunMissingHostJsonTest("dotnet-isolated", nameof(Start_Dotnet_Isolated_MissingHostJson_FailsWithExpectedError));
        }

        [Theory]
        [InlineData("dotnet-isolated", "--dotnet-isolated", "HttpTriggerFunc: [GET,POST] http://localhost:", true, false)] // Runtime parameter set (dni), successful startup & invocation
        [InlineData("node", "--node", "HttpTriggerFunc: [GET,POST] http://localhost:", true, false)] // Runtime parameter set (node), successful startup & invocation
        [InlineData("dotnet-isolated", "", "HttpTriggerFunc: [GET,POST] http://localhost:", true, true)] // Runtime value is set via environment variable, successful startup & invocation
        public async Task Start_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, string expectedOutput, bool invokeFunction, bool setRuntimeViaEnvironment)
        {
            await RunMissingLocalSettingsJsonTest(language, runtimeParameter, expectedOutput, invokeFunction, setRuntimeViaEnvironment, nameof(Start_MissingLocalSettingsJson_BehavesAsExpected));
        }

        [Fact]
        public async Task Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError()
        {
            var port = ProcessHelper.GetAvailablePort();
            var functionName = "HttpTriggerJS";
            var testName = nameof(Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError);

            // Initialize Node.js function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "-m", "v3" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", functionName, "--language", "node" }, workerRuntime: "node");

            // Modify function.json to include an invalid binding type
            var filePath = Path.Combine(WorkingDirectory, functionName, "function.json");
            var functionJson = await File.ReadAllTextAsync(filePath);
            functionJson = functionJson.Replace("\"type\": \"http\"", "\"type\": \"http2\"");
            await File.WriteAllTextAsync(filePath, functionJson);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                .Execute(new[] { "--port", port.ToString(), "--verbose" });

            // Validate error message
            result.Should().HaveStdOutContaining("The binding type(s) 'http2' were not found in the configured extension bundle. Please ensure the type is correct and the correct version of extension bundle is configured.");
        }

        [Fact]
        public async Task Start_EmptyEnvVars_HandledAsExpected()
        {
            var port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_EmptyEnvVars_HandledAsExpected);

            // Initialize Node.js function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "node", "-m", "v4" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "HttpTrigger", "--language", "node" }, workerRuntime: "node");

            // Add empty setting
            var funcSettingsResult = new FuncSettingsCommand(FuncPath, testName, Log)
                                    .WithWorkingDirectory(WorkingDirectory)
                                    .Execute(new[] { "add", "emptySetting", "EMPTY_VALUE" });
            funcSettingsResult.Should().ExitWith(0);

            // Modify settings file to have empty value
            var settingsPath = Path.Combine(WorkingDirectory, "local.settings.json");
            var settingsContent = File.ReadAllText(settingsPath);
            settingsContent = settingsContent.Replace("EMPTY_VALUE", string.Empty);
            File.WriteAllText(settingsPath, settingsContent);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter);
            };

            var result = funcStartCommand
                        .WithWorkingDirectory(WorkingDirectory)
                        .WithEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "node")
                        .Execute(new[] { "--port", port.ToString(), "--verbose" });

            // Validate function works and doesn't show skipping message
            result.Should().NotHaveStdOutContaining("Skipping 'emptySetting' from local settings as it's already defined in current environment variables.");
        }
    }
}
