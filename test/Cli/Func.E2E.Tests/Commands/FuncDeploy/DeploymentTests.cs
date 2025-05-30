// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Commands;
using Azure.Functions.Cli.TestFramework.Assertions;
using Azure.Functions.Cli.E2E.Tests.Traits;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Azure.Functions.Cli.E2E.Tests.Commands.FuncDeploy
{
    /// <summary>
    /// Tests for func azure functionapp publish command (deployment functionality).
    /// Note: These tests require Azure resources and appropriate environment variables to be set.
    /// </summary>
    [Trait(TestTraits.Group, "Deploy")]
    public class DeploymentTests : BaseE2ETests
    {
        public DeploymentTests(ITestOutputHelper log) : base(log)
        {
        }

        [SkippableFact]
        [Trait(WorkerRuntimeTraits.WorkerRuntime, WorkerRuntimeTraits.Python)]
        public async Task RemoteBuildPythonFunctionApp()
        {
            // Check required environment variables for Azure deployment
            var subscriptionId = Environment.GetEnvironmentVariable("TEST_SUBSCRIPTION_ID");
            var resourceGroupLinux = Environment.GetEnvironmentVariable("TEST_RESOURCE_GROUP_NAME_LINUX");
            var enableDeploymentTests = Environment.GetEnvironmentVariable("ENABLE_DEPLOYMENT_TEST");
            var isPublicBuild = Environment.GetEnvironmentVariable("IsPublicBuild");
            var azureServicePrincipalId = Environment.GetEnvironmentVariable("AZURE_SERVICE_PRINCIPAL_ID");
            var azureServicePrincipalKey = Environment.GetEnvironmentVariable("AZURE_SERVICE_PRINCIPAL_KEY");
            var azureDirectoryId = Environment.GetEnvironmentVariable("AZURE_DIRECTORY_ID");

            // Skip test if not in deployment test environment
            Skip.If(string.IsNullOrEmpty(subscriptionId), "TEST_SUBSCRIPTION_ID is not set");
            Skip.If(string.IsNullOrEmpty(resourceGroupLinux), "TEST_RESOURCE_GROUP_NAME_LINUX is not set");
            Skip.If(enableDeploymentTests != "true", "ENABLE_DEPLOYMENT_TEST is not set to true");
            Skip.If(isPublicBuild == "true", "IsPublicBuild is set to true");
            Skip.If(string.IsNullOrEmpty(azureServicePrincipalId), "AZURE_SERVICE_PRINCIPAL_ID is not set");
            Skip.If(string.IsNullOrEmpty(azureServicePrincipalKey), "AZURE_SERVICE_PRINCIPAL_KEY is not set");
            Skip.If(string.IsNullOrEmpty(azureDirectoryId), "AZURE_DIRECTORY_ID is not set");

            string methodName = "RemoteBuildPythonFunctionApp";
            string uniqueTestName = $"{methodName}_{DateTime.Now:HHmmss}";
            
            // TODO: Implement Azure resource management for the new test framework
            // This should create Azure resources similar to the old test framework:
            // 1. Create storage account: $"lcte2estorage{id}"  
            // 2. Create App Service Plan: $"lcte2ecserverfarm{id}"
            // 3. Create Function App: $"lcte2ecpython{id}"
            // 4. Wait for resources to be available and ready
            // 
            // For reference, see the old implementation in:
            // test/Azure.Functions.Cli.Tests/E2E/DeploymentTests.cs InitializeLinuxResources()
            // test/Azure.Functions.Cli.Tests/E2E/AzureResourceManagers/
            
            // Generate a unique function app name for this test run
            string id = DateTime.Now.ToString("HHmmss");
            string functionAppName = $"lcte2ecpython{id}";

            Log.WriteLine($"Test will attempt to deploy to function app: {functionAppName}");
            Log.WriteLine("Note: Azure resource creation not yet implemented in new test framework");
            Log.WriteLine("Required environment variables checked: TEST_SUBSCRIPTION_ID, TEST_RESOURCE_GROUP_NAME_LINUX, AZURE_SERVICE_PRINCIPAL_*");

            try
            {
                // Initialize Python function app  
                // This follows the same sequence as the original test:
                // 1. func init . --worker-runtime python
                // 2. func new -l python -t HttpTrigger -n httptrigger  
                // 3. func azure functionapp publish {app} --build remote
                await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "python" });
                await FuncNewWithRetryAsync(uniqueTestName, new[] { ".", "-l", "python", "-t", "HttpTrigger", "-n", "httptrigger" });

                // Create deploy command using the new framework pattern
                // Note: Unlike func start, deployment doesn't need a ProcessStartedHandler
                // since it's a one-shot command rather than a long-running process
                var funcDeployCommand = new FuncDeployCommand(FuncPath, methodName, Log);

                // Build command arguments for func azure functionapp publish with remote build
                var commandArgs = new List<string> 
                { 
                    "functionapp", 
                    "publish", 
                    functionAppName, 
                    "--build", 
                    "remote" 
                };

                // Execute the deployment command  
                // Note: Deployment commands typically take longer than other commands (5+ minutes)
                // The old framework used CommandTimeout = TimeSpan.FromMinutes(5)
                // TODO: Add timeout support to the new test framework if needed
                var result = funcDeployCommand
                    .WithWorkingDirectory(WorkingDirectory)
                    .Execute(commandArgs.ToArray());

                // Validate the deployment succeeded
                result.Should().ExitWith(0);
                result.Should().HaveStdOutContaining("Remote build succeeded!");

                Log.WriteLine("Deployment test completed successfully");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Deployment test failed: {ex.Message}");
                throw;
            }
        }
    }
}