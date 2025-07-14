// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Functions.Cli.Actions.LocalActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Interfaces;
using Moq;
using Xunit;

namespace Azure.Functions.Cli.Tests.ActionsTests
{
    public class PackActionTests
    {
        [Fact]
        public async Task PackAction_DotnetInProcess_DoesNotThrowException()
        {
            // Arrange
            var secretsManagerMock = new Mock<ISecretsManager>();
            var packAction = new PackAction(secretsManagerMock.Object);
            
            // Create a temporary directory with minimal function app structure
            var tempDir = Path.GetTempFileName();
            File.Delete(tempDir);
            Directory.CreateDirectory(tempDir);
            
            try
            {
                // Create host.json
                var hostJsonPath = Path.Combine(tempDir, "host.json");
                await File.WriteAllTextAsync(hostJsonPath, "{}");
                
                // Create local.settings.json with .NET runtime
                var localSettingsPath = Path.Combine(tempDir, "local.settings.json");
                await File.WriteAllTextAsync(localSettingsPath, @"{
  ""IsEncrypted"": false,
  ""Values"": {
    ""FUNCTIONS_WORKER_RUNTIME"": ""dotnet""
  }
}");

                // Set the current worker runtime to dotnet (in-process)
                Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "dotnet");
                GlobalCoreToolsSettings.CurrentWorkerRuntime = WorkerRuntime.Dotnet;
                
                packAction.FolderName = tempDir;
                packAction.OutputPath = Path.Combine(tempDir, "test.zip");

                // Act & Assert - This should not throw an exception anymore
                // Before the fix, this would throw "Pack command doesn't work for dotnet functions"
                var exception = await Record.ExceptionAsync(() => packAction.RunAsync());
                
                // The actual pack operation might fail due to missing extensions or other issues,
                // but it should NOT fail with the "Pack command doesn't work for dotnet functions" error
                if (exception != null)
                {
                    Assert.DoesNotContain("Pack command doesn't work for dotnet functions", exception.Message);
                }
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", null);
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }
}