// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.Text;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.ConfigurationProfiles;
using Azure.Functions.Cli.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ConfigurationProfileTests
{
    public class McpCustomHandlerConfigurationProfileTests : IDisposable
    {
        private readonly IFileSystem _originalFileSystem;
        private readonly McpCustomHandlerConfigurationProfile _profile;

        public McpCustomHandlerConfigurationProfileTests()
        {
            _originalFileSystem = FileSystemHelpers.Instance;
            _profile = new McpCustomHandlerConfigurationProfile();
        }

        [Fact]
        public void Name_ReturnsCorrectValue()
        {
            // Assert
            _profile.Name.Should().Be("mcp-custom-handler");
        }

        private static IFileSystem GetMockFileSystem(string hostJsonContent, string localSettingsContent, bool hostJsonExists = true, bool localSettingsExists = true)
        {
            var fileSystem = Substitute.For<IFileSystem>();

            fileSystem.File.Exists(Arg.Any<string>()).Returns(ci =>
            {
                var path = ci.ArgAt<string>(0);
                if (path.EndsWith("host.json"))
                {
                    return hostJsonExists;
                }
                else if (path.EndsWith("local.settings.json"))
                {
                    return localSettingsExists;
                }

                return false;
            });

            // Setup read streams for existing files - return NEW stream for each call
            if (hostJsonExists && hostJsonContent != null)
            {
                fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("host.json")), FileMode.Open, FileAccess.Read, Arg.Any<FileShare>())
                    .Returns(ci => hostJsonContent.ToStream());
            }

            if (localSettingsExists && localSettingsContent != null)
            {
                fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("local.settings.json")), FileMode.Open, FileAccess.Read, Arg.Any<FileShare>())
                    .Returns(ci => localSettingsContent.ToStream());
            }

            return fileSystem;
        }

        [Fact]
        public async Task ApplyHostJsonAsync_CreatesNewFile_WhenFileDoesNotExist()
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);

            var writeStream = new MemoryStream();

            fileSystem.File.Open(
                Arg.Is<string>(p => p.EndsWith("host.json")),
                FileMode.Create,
                FileAccess.Write,
                Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyHostJsonAsync(false);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            fileSystem.File.Received(1).Open(
                Arg.Is<string>(s => s.EndsWith("host.json")),
                FileMode.Create,
                FileAccess.Write,
                Arg.Any<FileShare>());

            var writtenContent = Encoding.UTF8.GetString(writtenBytes);

            var hostJson = JObject.Parse(writtenContent);
            hostJson["configurationProfile"]?.ToString().Should().Be("mcp-custom-handler");
            hostJson["customHandler"]?.Should().NotBeNull();
            hostJson["customHandler"]?["description"]?.Should().NotBeNull();
        }

        [Fact]
        public async Task ApplyHostJsonAsync_UpdatesExistingFile_WhenFileExists()
        {
            // Arrange
            var existingHostJson = @"{""version"": ""2.0""}";
            var fileSystem = GetMockFileSystem(existingHostJson, null, hostJsonExists: true, localSettingsExists: false);
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("host.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyHostJsonAsync(false);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var hostJson = JObject.Parse(writtenContent);
            hostJson["version"]?.ToString().Should().Be("2.0");
            hostJson["configurationProfile"]?.ToString().Should().Be("mcp-custom-handler");
            hostJson["customHandler"]?.Should().NotBeNull();
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
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("host.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyHostJsonAsync(force: true);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var hostJson = JObject.Parse(writtenContent);
            hostJson["configurationProfile"]?.ToString().Should().Be("mcp-custom-handler");
            hostJson["customHandler"]?["description"]?["defaultExecutablePath"]?.ToString().Should().BeEmpty();
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

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyHostJsonAsync(force: false);

            // Assert
            fileSystem.File.DidNotReceive().Open(
                Arg.Any<string>(),
                FileMode.Create,
                FileAccess.Write,
                Arg.Any<FileShare>());
        }

        [Fact]
        public async Task ApplyLocalSettingsAsync_CreatesNewFile_WhenFileDoesNotExist()
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("local.settings.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, false);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var localSettings = JObject.Parse(writtenContent);
            localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be("node");
            localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString().Should().Be("EnableMcpCustomHandlerPreview");
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
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("local.settings.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, false);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var localSettings = JObject.Parse(writtenContent);
            localSettings["Values"]?["AzureWebJobsStorage"]?.ToString().Should().Be("UseDevelopmentStorage=true");
            localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be("node");
            localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString().Should().Be("EnableMcpCustomHandlerPreview");
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
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("local.settings.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: true);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var localSettings = JObject.Parse(writtenContent);
            var flags = localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString();
            flags.Should().Be("ExistingFlag1,ExistingFlag2,EnableMcpCustomHandlerPreview");
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
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("local.settings.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: true);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var localSettings = JObject.Parse(writtenContent);
            var flags = localSettings["Values"]?["AzureWebJobsFeatureFlags"]?.ToString();
            flags.Should().Be("ExistingFlag,EnableMcpCustomHandlerPreview,AnotherFlag");
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
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("local.settings.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: true);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var localSettings = JObject.Parse(writtenContent);
            localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be("node");
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

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyLocalSettingsAsync(WorkerRuntime.Node, force: false);

            // Assert
            fileSystem.File.DidNotReceive().Open(
                Arg.Any<string>(),
                FileMode.Create,
                FileAccess.Write,
                Arg.Any<FileShare>());
        }

        [Theory]
        [InlineData(WorkerRuntime.Node, "node")]
        [InlineData(WorkerRuntime.Python, "python")]
        [InlineData(WorkerRuntime.DotnetIsolated, "dotnet-isolated")]
        public async Task ApplyLocalSettingsAsync_SetsCorrectWorkerRuntime(WorkerRuntime runtime, string expectedMoniker)
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);
            var writeStream = new MemoryStream();
            fileSystem.File.Open(Arg.Is<string>(p => p.EndsWith("local.settings.json")), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(writeStream);

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyLocalSettingsAsync(runtime, false);

            // Capture bytes BEFORE asserting (stream might be closed)
            var writtenBytes = writeStream.ToArray();

            // Assert
            var writtenContent = Encoding.UTF8.GetString(writtenBytes);
            var localSettings = JObject.Parse(writtenContent);
            localSettings["Values"]?["FUNCTIONS_WORKER_RUNTIME"]?.ToString().Should().Be(expectedMoniker);
        }

        [Theory]
        [InlineData(WorkerRuntime.Node)]
        [InlineData(WorkerRuntime.Python)]
        [InlineData(WorkerRuntime.DotnetIsolated)]
        public async Task ApplyAsync_Succeeds(WorkerRuntime runtime)
        {
            // Arrange
            var fileSystem = GetMockFileSystem(null, null, hostJsonExists: false, localSettingsExists: false);

            var hostJsonWritten = false;
            var localSettingsWritten = false;

            fileSystem.File.Open(Arg.Any<string>(), FileMode.Create, FileAccess.Write, Arg.Any<FileShare>())
                .Returns(ci =>
                {
                    var path = ci.ArgAt<string>(0);
                    if (path.EndsWith("host.json"))
                    {
                        hostJsonWritten = true;
                    }
                    else if (path.EndsWith("local.settings.json"))
                    {
                        localSettingsWritten = true;
                    }

                    return new MemoryStream();
                });

            FileSystemHelpers.Instance = fileSystem;

            // Act
            await _profile.ApplyAsync(runtime, false);

            // Assert
            hostJsonWritten.Should().BeTrue();
            localSettingsWritten.Should().BeTrue();
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = _originalFileSystem;
        }
    }
}
