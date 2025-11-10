// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ConfigurationProfiles;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ConfigurationProfileTests
{
    public class McpCustomHandlerConfigurationProfileTests
    {
        private readonly McpCustomHandlerConfigurationProfile _profile = new();

        [Fact]
        public void Name_ReturnsCorrectValue()
        {
            _profile.Name.Should().Be("mcp-custom-handler");
        }

        [Fact]
        public async Task ApplyHostJsonAsync_CreatesNewFile_WhenFileDoesNotExist()
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);
            var hostCap = TestUtilities.SetupWriteFor(fileSystem, "host.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyHostJsonAsync(false);

                // Assert
                var hostJson = JObject.Parse(hostCap.LastText());
                hostJson["configurationProfile"]?.ToString().Should().Be("mcp-custom-handler");
                hostJson["customHandler"]?.Should().NotBeNull();
                hostJson["customHandler"]?["description"]?.Should().NotBeNull();
            }
        }

        [Fact]
        public async Task ApplyHostJsonAsync_UpdatesExistingFile_WhenFileExists()
        {
            // Arrange
            var existingHostJson = @"{""version"": ""2.0""}";
            var fileSystem = GetMockFileSystem(existingHostJson, null, hostJsonExists: true, localSettingsExists: false);
            var hostCap = TestUtilities.SetupWriteFor(fileSystem, "host.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyHostJsonAsync(false);

                // Assert
                var hostJson = JObject.Parse(hostCap.LastText());
                hostJson["version"]?.ToString().Should().Be("2.0");
                hostJson["configurationProfile"]?.ToString().Should().Be("mcp-custom-handler");
                hostJson["customHandler"]?.Should().NotBeNull();
            }
        }

        [Fact]
        public async Task ApplyHostJsonAsync_WithForce_OverwritesExistingConfiguration()
        {
            // Arrange
            var existingHostJson = @"{
                ""version"": ""2.0"",
                ""configurationProfile"": ""other-profile"",
                ""customHandler"": {
                    ""description"": {
                        ""defaultExecutablePath"": ""existing.exe"",
                        ""arguments"": [""arg1""]
                    }
                }
            }";

            var fileSystem = GetMockFileSystem(existingHostJson, null, hostJsonExists: true, localSettingsExists: false);
            var hostCap = TestUtilities.SetupWriteFor(fileSystem, "host.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyHostJsonAsync(force: true);

                // Assert
                var hostJson = JObject.Parse(hostCap.LastText());
                hostJson["configurationProfile"]?.ToString().Should().Be("mcp-custom-handler");
                hostJson["customHandler"]?["description"]?["defaultExecutablePath"]?.ToString().Should().BeEmpty();
            }
        }

        [Fact]
        public async Task ApplyHostJsonAsync_WithoutForce_DoesNotOverwriteExistingConfiguration()
        {
            // Arrange
            var existingHostJson = @"{
                ""version"": ""2.0"",
                ""configurationProfile"": ""mcp-custom-handler"",
                ""customHandler"": {
                    ""description"": {
                        ""defaultExecutablePath"": ""existing.exe""
                    }
                }
            }";

            var fileSystem = GetMockFileSystem(existingHostJson, null, hostJsonExists: true, localSettingsExists: false);
            var hostCap = TestUtilities.SetupWriteFor(fileSystem, "host.json"); // set up so asserts can check no writes

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyHostJsonAsync(force: false);

                // Assert
                hostCap.Streams.Should().BeEmpty("no write should occur without force when config already matches");
            }
        }

        [Fact]
        public async Task ApplyLocalSettingsAsync_CreatesNewFile_WhenFileDoesNotExist()
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, false);

                // Assert
                var localSettings = JObject.Parse(localCap.LastText());
                localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be("node");
                localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString().Should().Be("EnableMcpCustomHandlerPreview");
            }
        }

        [Fact]
        public async Task ApplyLocalSettingsAsync_UpdatesExistingFile_WhenFileExists()
        {
            // Arrange
            var existingLocalSettings = @"{
                ""IsEncrypted"": false,
                ""Values"": {
                    ""AzureWebJobsStorage"": ""UseDevelopmentStorage=true""
                }
            }";

            var fileSystem = GetMockFileSystem(null, existingLocalSettings, hostJsonExists: false, localSettingsExists: true);
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, false);

                // Assert
                var localSettings = JObject.Parse(localCap.LastText());
                localSettings["Values"]?["AzureWebJobsStorage"]?.ToString().Should().Be("UseDevelopmentStorage=true");
                localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be("node");
                localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString().Should().Be("EnableMcpCustomHandlerPreview");
            }
        }

        [Fact]
        public async Task ApplyLocalSettingsAsync_AppendsFeatureFlag_WhenFlagsAlreadyExist()
        {
            // Arrange
            var existingLocalSettings = @"{
                ""IsEncrypted"": false,
                ""Values"": {
                    ""FUNCTIONS_WORKER_RUNTIME"": ""node"",
                    ""AzureWebJobsFeatureFlags"": ""ExistingFlag1,ExistingFlag2""
                }
            }";

            var fileSystem = GetMockFileSystem(null, existingLocalSettings, hostJsonExists: false, localSettingsExists: true);
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: true);

                // Assert
                var localSettings = JObject.Parse(localCap.LastText());
                var flags = localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString();
                flags.Should().Be("ExistingFlag1,ExistingFlag2,EnableMcpCustomHandlerPreview");
            }
        }

        [Fact]
        public async Task ApplyLocalSettingsAsync_DoesNotDuplicateFeatureFlag_WhenAlreadyPresent()
        {
            // Arrange
            var existingLocalSettings = @"{
                ""IsEncrypted"": false,
                ""Values"": {
                    ""FUNCTIONS_WORKER_RUNTIME"": ""node"",
                    ""AzureWebJobsFeatureFlags"": ""ExistingFlag,EnableMcpCustomHandlerPreview,AnotherFlag""
                }
            }";

            var fileSystem = GetMockFileSystem(null, existingLocalSettings, hostJsonExists: false, localSettingsExists: true);
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: true);

                // Assert
                var localSettings = JObject.Parse(localCap.LastText());
                var flags = localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString();
                flags.Should().Be("ExistingFlag,EnableMcpCustomHandlerPreview,AnotherFlag");
            }
        }

        [Fact]
        public async Task ApplyLocalSettingsAsync_WithForce_UpdatesWorkerRuntime()
        {
            // Arrange
            var existingLocalSettings = @"{
                ""IsEncrypted"": false,
                ""Values"": {
                    ""FUNCTIONS_WORKER_RUNTIME"": ""dotnet""
                }
            }";

            var fileSystem = GetMockFileSystem(null, existingLocalSettings, hostJsonExists: false, localSettingsExists: true);
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: true);

                // Assert
                var localSettings = JObject.Parse(localCap.LastText());
                localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be("node");
            }
        }

        [Fact]
        public async Task ApplyLocalSettingsAsync_WithoutForce_DoesNotUpdateExistingWorkerRuntime()
        {
            // Arrange
            var existingLocalSettings = @"{
                ""IsEncrypted"": false,
                ""Values"": {
                    ""FUNCTIONS_WORKER_RUNTIME"": ""dotnet"",
                    ""AzureWebJobsFeatureFlags"": ""EnableMcpCustomHandlerPreview""
                }
            }";

            var fileSystem = GetMockFileSystem(null, existingLocalSettings, hostJsonExists: false, localSettingsExists: true);
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json"); // so we can assert no writes

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: false);

                // Assert
                localCap.Streams.Should().BeEmpty("no write should occur without force when worker runtime already set");
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Node, "node")]
        [InlineData(WorkerRuntime.Python, "python")]
        [InlineData(WorkerRuntime.DotnetIsolated, "dotnet-isolated")]
        public async Task ApplyLocalSettingsAsync_SetsCorrectWorkerRuntime(WorkerRuntime runtime, string expectedMoniker)
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyLocalSettingsAsync(runtime, false);

                // Assert
                var localSettings = JObject.Parse(localCap.LastText());
                localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be(expectedMoniker);
            }
        }

        [Theory]
        [InlineData(WorkerRuntime.Node)]
        [InlineData(WorkerRuntime.Python)]
        [InlineData(WorkerRuntime.DotnetIsolated)]
        public async Task ApplyAsync_Succeeds(WorkerRuntime runtime)
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);
            var hostCap = TestUtilities.SetupWriteFor(fileSystem, "host.json");
            var localCap = TestUtilities.SetupWriteFor(fileSystem, "local.settings.json");

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Act
                await _profile.ApplyAsync(runtime, false);

                // Assert
                hostCap.Streams.Should().NotBeEmpty("host.json should be written");
                localCap.Streams.Should().NotBeEmpty("local.settings.json should be written");
            }
        }

        /// <summary>
        /// Creates a mock IFileSystem that:
        ///  - Answers File.Exists for host.json/local.settings.json
        ///  - Returns a NEW MemoryStream each time File.Open(..., Read) is called for those files.
        /// </summary>
        private static IFileSystem GetMockFileSystem(string hostJsonContent, string localSettingsContent, bool hostJsonExists = true, bool localSettingsExists = true)
        {
            var fileSystem = Substitute.For<IFileSystem>();

            fileSystem.File.Exists(Arg.Any<string>()).Returns(ci =>
            {
                var path = ci.ArgAt<string>(0);
                if (path.EndsWith("host.json", StringComparison.Ordinal))
                {
                    return hostJsonExists;
                }
                else if (path.EndsWith("local.settings.json", StringComparison.Ordinal))
                {
                    return localSettingsExists;
                }

                return false;
            });

            // Setup READ streams for existing files - return NEW stream for each call
            if (hostJsonExists && hostJsonContent != null)
            {
                fileSystem.File.Open(
                        Arg.Is<string>(p => p.EndsWith("host.json", StringComparison.Ordinal)),
                        FileMode.Open,
                        FileAccess.Read,
                        Arg.Any<FileShare>())
                    .Returns(_ => hostJsonContent.ToStream());
            }

            if (localSettingsExists && localSettingsContent != null)
            {
                fileSystem.File.Open(
                        Arg.Is<string>(p => p.EndsWith("local.settings.json", StringComparison.Ordinal)),
                        FileMode.Open,
                        FileAccess.Read,
                        Arg.Any<FileShare>())
                    .Returns(_ => localSettingsContent.ToStream());
            }

            return fileSystem;
        }
    }
}
