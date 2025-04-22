// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Helpers;
using FluentAssertions;
using Func.E2ETests.Traits;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart.Core
{
    public class BaseUserSecretsTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        public async Task RunUserSecretsTest(string language, string testName)
        {
            var port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", language });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "http1" });

            // Add Queue trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "QueueTrigger", "--name", "queue1" });

            // Modify queue code to use connection string
            var queueCodePath = Path.Combine(WorkingDirectory, "queue1.cs");
            var queueCode = File.ReadAllText(queueCodePath);
            queueCode = queueCode.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
            File.WriteAllText(queueCodePath, queueCode);

            // Clear local.settings.json
            var settingsPath = Path.Combine(WorkingDirectory, "local.settings.json");
            var settingsContent = "{ \"IsEncrypted\": false, \"Values\": { \"FUNCTIONS_WORKER_RUNTIME\": \"" + language + "\", \"AzureWebJobsSecretStorageType\": \"files\"} }";
            File.WriteAllText(settingsPath, settingsContent);

            // Set up user secrets
            var userSecrets = new Dictionary<string, string>
            {
                { "AzureWebJobsStorage", "UseDevelopmentStorage=true" },
                { "ConnectionStrings:MyQueueConn", "UseDevelopmentStorage=true" }
            };

            SetupUserSecrets(userSecrets);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                try
                {
                    await ProcessHelper.WaitForFunctionHostToStart(process, port, funcStartCommand.FileWriter);

                    // Insert message into queue
                    await QueueStorageHelper.InsertIntoQueue("myqueue-items", "hello world");
                }
                finally
                {
                    process.Kill(true);
                }
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "start", "--build", "--port", port.ToString() });

            // Validate user secrets are used
            result.Should().HaveStdOutContaining("Using for user secrets file configuration.");
        }

        public async Task RunMissingStorageConnString_FailsWithExpectedError(string languageWorker, string testName)
        {
            var azureWebJobsStorage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (!string.IsNullOrEmpty(azureWebJobsStorage))
            {
                Log.WriteLine("Skipping test as AzureWebJobsStorage is set");
                return;
            }

            int port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", languageWorker });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "http1" });

            // Add Queue trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "QueueTrigger", "--name", "queue1" });

            // Modify queue code to use connection string
            var queueCodePath = Path.Combine(WorkingDirectory, "queue1.cs");
            var queueCode = File.ReadAllText(queueCodePath);
            queueCode = queueCode.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
            File.WriteAllText(queueCodePath, queueCode);

            // Clear local.settings.json
            var settingsPath = Path.Combine(WorkingDirectory, "local.settings.json");
            var settingsContent = "{ \"IsEncrypted\": false, \"Values\": { \"FUNCTIONS_WORKER_RUNTIME\": \"" + languageWorker + "\"} }";
            File.WriteAllText(settingsPath, settingsContent);

            // Set up user secrets with missing AzureWebJobsStorage
            var userSecrets = new Dictionary<string, string>
            {
                { "ConnectionStrings:MyQueueConn", "UseDevelopmentStorage=true" }
            };

            SetupUserSecrets(userSecrets);

            // Call func start for HTTP function only
            var result = new FuncStartCommand(FuncPath, testName, Log)
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "start", "--functions", "http1", "--port", port.ToString() });

            // Validate error message
            result.Should().HaveStdOutContaining("Missing value for AzureWebJobsStorage in local.settings.json");
            result.Should().HaveStdOutContaining("A host error has occurred during startup operation");
        }

        public async Task RunWithUserSecrets_MissingBindingSetting_FailsWithExpectedError(string languageWorker, string testName)
        {
            var azureWebJobsStorage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (!string.IsNullOrEmpty(azureWebJobsStorage))
            {
                Log.WriteLine("Skipping test as AzureWebJobsStorage is set");
                return;
            }

            var port = ProcessHelper.GetAvailablePort();

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", languageWorker });

            // Add HTTP trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "Httptrigger", "--name", "http1" });

            // Add Queue trigger using retry helper
            await FuncNewWithRetryAsync(testName, new[] { ".", "--template", "QueueTrigger", "--name", "queue1" });

            // Modify queue code to use connection string
            var queueCodePath = Path.Combine(WorkingDirectory, "queue1.cs");
            var queueCode = File.ReadAllText(queueCodePath);
            queueCode = queueCode.Replace("Connection = \"\"", "Connection = \"ConnectionStrings:MyQueueConn\"");
            File.WriteAllText(queueCodePath, queueCode);

            // Clear local.settings.json
            var settingsPath = Path.Combine(WorkingDirectory, "local.settings.json");
            var settingsContent = "{ \"IsEncrypted\": false, \"Values\": { \"FUNCTIONS_WORKER_RUNTIME\": \"" + languageWorker + "\"} }"; ;
            File.WriteAllText(settingsPath, settingsContent);

            // Set up user secrets with AzureWebJobsStorage but missing MyQueueConn
            var userSecrets = new Dictionary<string, string>
            {
                { "AzureWebJobsStorage", "UseDevelopmentStorage=true" }
            };

            SetupUserSecrets(userSecrets);

            // Call func start
            var funcStartCommand = new FuncStartCommand(FuncPath, testName, Log);

            funcStartCommand.ProcessStartedHandler = async (process) =>
            {
                await ProcessHelper.ProcessStartedHandlerHelper(port, process, funcStartCommand.FileWriter);
            };

            var result = funcStartCommand
                .WithWorkingDirectory(WorkingDirectory)
                .Execute(new[] { "--port", port.ToString() });

            // Validate warning message about missing connection string
            result.Should().HaveStdOutContaining("Warning: Cannot find value named 'ConnectionStrings:MyQueueConn' in local.settings.json");
            result.Should().HaveStdOutContaining("You can run 'func azure functionapp fetch-app-settings <functionAppName>' or specify a connection string in local.settings.json.");
        }

        private void SetupUserSecrets(Dictionary<string, string> secrets)
        {
            // Initialize user secrets
            var initProcess = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "user-secrets init",
                WorkingDirectory = WorkingDirectory,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using (var process = Process.Start(initProcess))
                process?.WaitForExit();

            // Set each secret
            foreach (var secret in secrets)
            {
                var setProcess = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"user-secrets set \"{secret.Key}\" \"{secret.Value}\"",
                    WorkingDirectory = WorkingDirectory,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(setProcess))
                    process?.WaitForExit();
            }
        }

        [Theory]
        [InlineData("dotnet-isolated", "--dotnet-isolated", true, false)]
        [InlineData("node", "--node", true, false)]
        [InlineData("dotnet-isolated", "", true, true)]
        public async Task Start_MissingLocalSettingsJson_BehavesAsExpected(string language, string runtimeParameter, bool invokeFunction, bool setRuntimeViaEnvironment)
        {
            try
            {
                var methodName = nameof(Start_MissingLocalSettingsJson_BehavesAsExpected);
                var logFileName = $"{methodName}_{language}_{runtimeParameter}";
                if (setRuntimeViaEnvironment)
                    Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet-isolated");

                var port = ProcessHelper.GetAvailablePort();

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
