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

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncStart
{
    public class UserSecretsTests(ITestOutputHelper log) : BaseE2ETests(log)
    {
        [Theory]
        [InlineData("dotnet")]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_Dotnet_WithUserSecrets_SuccessfulFunctionExecution(string language)
        {
            int port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_Dotnet_WithUserSecrets_SuccessfulFunctionExecution);

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

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_Dotnet_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError()
        {
            var azureWebJobsStorage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (!string.IsNullOrEmpty(azureWebJobsStorage))
            {
                Log.WriteLine("Skipping test as AzureWebJobsStorage is set");
                return;
            }

            int port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_Dotnet_WithUserSecrets_MissingStorageConnString_FailsWithExpectedError);

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "dotnet" });

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
            var settingsContent = "{ \"IsEncrypted\": false, \"Values\": { \"FUNCTIONS_WORKER_RUNTIME\": \"dotnet\"} }";
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

        [Fact]
        [Trait(TestTraits.Group, TestTraits.RequiresNestedInProcArtifacts)]
        public async Task Start_Dotnet_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError()
        {
            var azureWebJobsStorage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (!string.IsNullOrEmpty(azureWebJobsStorage))
            {
                Log.WriteLine("Skipping test as AzureWebJobsStorage is set");
                return;
            }

            int port = ProcessHelper.GetAvailablePort();
            var testName = nameof(Start_Dotnet_WithUserSecrets_MissingBindingSetting_FailsWithExpectedError);

            // Initialize dotnet function app using retry helper
            await FuncInitWithRetryAsync(testName, new[] { ".", "--worker-runtime", "dotnet" });

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
            var settingsContent = "{ \"IsEncrypted\": false, \"Values\": { \"FUNCTIONS_WORKER_RUNTIME\": \"dotnet\"} }";
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
    }
}
