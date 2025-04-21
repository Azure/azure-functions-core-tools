// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Func.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class MissingConfigTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_InvalidHostJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();
            var testName = "Start_InProc_InvalidHostJson_FailsWithExpectedError";

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "dotnet" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });

            // Create invalid host.json
            var hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            var hostJsonContent = "{ \"version\": \"2.0\", \"extensionBundle\": { \"id\": \"Microsoft.Azure.Functions.ExtensionBundle\", \"version\": \"[2.*, 3.0.0)\" }}";
            File.WriteAllText(hostJsonPath, hostJsonContent);

            // Call func start
            var result = new FuncStartCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            result.Should().HaveStdOutContaining("Extension bundle configuration should not be present");
        }

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_InProc_MissingHostJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();
            var testName = "Start_InProc_MissingHostJson_FailsWithExpectedError";

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "dotnet" });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "HttpTriggerCSharp" });

            // Delete host.json
            var hostJsonPath = Path.Combine(WorkingDirectory, "host.json");
            File.Delete(hostJsonPath);

            // Call func start
            var result = new FuncStartCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            // Validate error message
            result.Should().HaveStdOutContaining("Host.json file in missing");
        }

        [Theory]
        [InlineData("dotnet-isolated", "--dotnet-isolated", true, false)]
        [InlineData("node", "--node", true, false)]
        [InlineData("dotnet-isolated", "", true, true)]
        public async Task Start_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, bool invokeFunction, bool setRuntimeViaEnvironment)
        {
            try
            {
                var methodName = "Start_MissingLocalSettingsJson_BehavesAsExpected";
                var logFileName = $"{methodName}_{language}_{runtimeParameter}";
                if (setRuntimeViaEnvironment)
                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated");

                int port = ProcessHelper.GetAvailablePort();

                // Initialize function app using retry helper
                await FuncInitWithRetryAsync(logFileName, new[] { ".", "--worker-runtime", language });

                var funcNewArgs = new[] { ".", "--template", "HttpTrigger", "--name", "HttpTriggerFunc" }
                                    .Concat(!language.Contains("dotnet") ? new[] { "--language", language } : Array.Empty<string>())
                                    .ToArray();

                // Add HTTP trigger using retry helper
                await FuncNewWithRetryAsync(logFileName, funcNewArgs);

                // Delete local.settings.json
                var localSettingsJson = Path.Combine(WorkingDirectory, "local.settings.json");
                File.Delete(localSettingsJson);

                // Call func start
                var funcStartCommand = new FuncStartCommand(FuncPath, logFileName, Log);

                funcStartCommand.ProcessStartedHandler = async (process) =>
                {
                    await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter, "HttpTriggerFunc");
                };

                var startCommand = new List<string> { "--port", port.ToString(), "--verbose" };
                if (!string.IsNullOrEmpty(runtimeParameter))
                    startCommand.Add(runtimeParameter);

                var result = funcStartCommand
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(startCommand.ToArray());

                // Validate output contains expected function URL
                if (invokeFunction)
                    result.Should().HaveStdOutContaining("HttpTriggerFunc: [GET,POST] http://localhost:");

                result.Should().HaveStdOutContaining("Executed 'Functions.HttpTriggerFunc' (Succeeded");
            }
            finally
            {
                // Clean up environment variable
                if (setRuntimeViaEnvironment)
                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", null);
            }
        }

        [Fact]
        public async Task Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError()
        {
            int port = ProcessHelper.GetAvailablePort();
            var functionName = "HttpTriggerJS";
            var testName = "Start_LanguageWorker_InvalidFunctionJson_FailsWithExpectedError";

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
            int port = ProcessHelper.GetAvailablePort();
            var testName = "Start_EmptyEnvVars_HandledAsExpected";

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
