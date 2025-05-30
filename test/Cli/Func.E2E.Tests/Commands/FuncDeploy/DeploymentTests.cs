// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Azure.Functions.Cli.TestFramework.Commands;
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
            // This should include:
            // 1. Storage account creation
            // 2. App Service Plan creation  
            // 3. Function App creation
            // 4. Waiting for resources to be ready
            // For now, we assume a function app exists with a predictable name
            string functionAppName = $"lcte2ecpython{DateTime.Now:HHmmss}";

            Log.WriteLine($"Test will attempt to deploy to function app: {functionAppName}");
            Log.WriteLine("Note: Azure resource creation not yet implemented in new test framework");

            try
            {
                // Initialize Python function app
                await FuncInitWithRetryAsync(uniqueTestName, new[] { ".", "--worker-runtime", "python" });
                await FuncNewWithRetryAsync(uniqueTestName, new[] { ".", "--template", "HttpTrigger", "--name", "httptrigger" });

                // Create deploy command using the new framework pattern
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