// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Azure.Functions.Cli.TestFramework.Helpers;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Fixtures
{
    /// <summary>
    /// Fixture for Durable Functions E2E tests.
    /// Ensures azurite is running and creates a Durable Functions app.
    /// </summary>
    public class DurableFunctionAppFixture : IAsyncLifetime
    {
        public DurableFunctionAppFixture()
        {
            Log = new Mock<ITestOutputHelper>().Object;
            FuncPath = Environment.GetEnvironmentVariable("FUNC_PATH") ?? string.Empty;

            // Fallback for local testing in Visual Studio, etc.
            FuncPath = Path.Combine(Environment.CurrentDirectory, "func");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                FuncPath += ".exe";
            }

            if (!File.Exists(FuncPath))
            {
                throw new ApplicationException("Could not locate the 'func' executable to use for testing. Make sure the FUNC_PATH environment variable is set to the full path of the func executable.");
            }

            Directory.CreateDirectory(WorkingDirectory);
        }

        public ITestOutputHelper Log { get; set; }

        public string FuncPath { get; set; }

        public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        public bool CleanupWorkingDirectory { get; set; } = true;

        public string StorageConnectionString { get; private set; } = "UseDevelopmentStorage=true";

        public Task DisposeAsync()
        {
            try
            {
                if (CleanupWorkingDirectory && Directory.Exists(WorkingDirectory))
                {
                    Directory.Delete(WorkingDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            return Task.CompletedTask;
        }

        public async Task InitializeAsync()
        {
            // Start Azurite if not already running
            if (!IsAzuriteRunning())
            {
                throw new ApplicationException("Azurite is not running. Please start Azurite before running the tests.");
            }

            // Create a new Durable Functions app
            await CreateDurableFunctionAppAsync();
        }

        private static bool IsAzuriteRunning()
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                client.Connect("127.0.0.1", 10000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task CreateDurableFunctionAppAsync()
        {
            // Scaffold a new function app with Durable Functions
            var workerRuntime = "dotnet-isolated";
            var initArgs = new[] { ".", "--worker-runtime", workerRuntime };
            await FunctionAppSetupHelper.FuncInitWithRetryAsync(FuncPath, "DurableApp", WorkingDirectory, Log, initArgs);

            // Add a sample Durable function (Orchestrator)
            var funcNewArgs = new[] { ".", "--template", "DurableFunctionsOrchestration", "--name", "DurableOrchestrator" };
            await FunctionAppSetupHelper.FuncNewWithRetryAsync(FuncPath, "DurableApp", WorkingDirectory, Log, funcNewArgs, workerRuntime);

            // Overwrite host.json with required durableTask hubName using direct string assignment
            var hostJsonContent =
                "{" +
                "\"extensions\":{" +
                "\"durableTask\":{" +
                "\"hubName\":\"MyTaskHub\"}" +
                "}," +
                "\"version\": \"2.0\"}";
            File.WriteAllText(Path.Combine(WorkingDirectory, "host.json"), hostJsonContent);
        }
    }
}
