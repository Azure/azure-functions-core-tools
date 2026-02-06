// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;
using Colors.Net;
using FluentAssertions;
using Moq;
using NSubstitute;
using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class StartHostActionTests
    {
        [SkippableFact]
        public async Task CheckNonOptionalSettings_ThrowsOnMissingAzureWebJobsStorageAndManagedIdentity()
        {
            Skip.IfNot(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");

            var fileSystem = GetFakeFileSystem(
            [
                ("x:\\folder1", "{'bindings': [{'type': 'blobTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            ]);

            fileSystem.File.Exists(Arg.Is<string>(s => s.EndsWith("local.settings.json"))).Returns(true);

            using (FileSystemHelpers.Override(fileSystem))
            {
                Exception exception = null;
                try
                {
                    await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), "x:\\", false);
                }
                catch (Exception e)
                {
                    exception = e;
                }

                exception.Should().NotBeNull();
                exception.Should().BeOfType<CliException>();
                exception!.Message.Should().Contain($"Missing value for AzureWebJobsStorage in local.settings.json. " +
                    $"This is required for all triggers other than {string.Join(", ", Common.Constants.TriggersWithoutStorage)}.");
            }
        }

        [Fact]
        public async Task CheckNonOptionalSettings_DoesntThrowMissingConnectionUsingManagedIdentity()
        {
            var fileSystem = GetFakeFileSystem(
            [
                ("x:\\folder1", "{'bindings': [{'type': 'serviceBusTrigger', 'connection': 'myServiceBusConnection'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'eventHubTrigger', 'connection': 'myEventHubConnection'}]}"),
                ("x:\\folder3", "{'bindings': [{'type': 'blobTrigger', 'connection': 'myBlobStorage'}]}"),
                ("x:\\folder4", "{'bindings': [{'type': 'queueTrigger', 'connection': 'myQueueStorage'}]}")
            ]);

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage", "myuri" },
                { "myServiceBusConnection", "myuri" },
                { "myEventHubConnection__fullyQualifiedNamespace", "myEHNamespace.servicebus.windows.net" },
                { "myBlobStorage__blobServiceUri", "myuri" },
                { "myQueueStorage__accountName", "myAccountName" }
            };

            using (FileSystemHelpers.Override(fileSystem))
            {
                Exception exception = null;
                try
                {
                    await StartHostAction.CheckNonOptionalSettings(secrets, "x:\\");
                }
                catch (Exception e)
                {
                    exception = e;
                }

                exception.Should().BeNull();
            }
        }

        [SkippableFact]
        public async Task CheckNonOptionalSettings_DoesntThrowMissingStorageUsingManagedIdentity()
        {
            Skip.IfNot(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");

            var fileSystem = GetFakeFileSystem(
            [
                ("x:\\folder1", "{'bindings': [{'type': 'blobTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            ]);

            fileSystem.File.Exists(Arg.Is<string>(s => s.EndsWith("local.settings.json"))).Returns(true);

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage:blobServiceUri", "myuri" },
                { "AzureWebJobsStorage__queueServiceUri", "queueuri" }
            };

            using (FileSystemHelpers.Override(fileSystem))
            {
                Exception exception = null;
                try
                {
                    await StartHostAction.CheckNonOptionalSettings(secrets, "x:\\");
                }
                catch (Exception e)
                {
                    exception = e;
                }

                exception.Should().BeNull();
            }
        }

        [SkippableFact]
        public async Task CheckNonOptionalSettings_DoesntThrowOnMissingAzureWebJobsStorage()
        {
            Skip.IfNot(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");

            var fileSystem = GetFakeFileSystem(
            [
                ("x:\\folder1", "{'bindings': [{'type': 'blobTrigger'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger'}]}")
            ]);

            fileSystem.File.Exists(Arg.Is<string>(s => s.EndsWith("local.settings.json"))).Returns(true);

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage:blobServiceUri", "myuri" },
                { "AzureWebJobsStorage__queueServiceUri", "queueuri" }
            };

            using (FileSystemHelpers.Override(fileSystem))
            {
                Exception exception = null;
                try
                {
                    await StartHostAction.CheckNonOptionalSettings(secrets, "x:\\", false);
                }
                catch (Exception e)
                {
                    exception = e;
                }

                exception.Should().BeNull();
            }
        }

        [Fact]
        public async Task CheckNonOptionalSettingsPrintsWarningForMissingSettings()
        {
            // Use an OS-appropriate fake root
            var root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "x:\\"
                : "/tmp/funcs/";

            // Build cross-platform folder paths
            var folder1 = Path.Combine(root, "folder1");
            var folder2 = Path.Combine(root, "folder2");

            var fileSystem = GetFakeFileSystem(
            [
                (folder1, "{'bindings': [{'type': 'httpTrigger', 'connection': 'blah'}]}"),
                (folder2, "{'bindings': [{'type': 'httpTrigger', 'connection': ''}]}")
            ]);

            fileSystem.File.Exists(Arg.Is<string>(s => s.EndsWith("local.settings.json"))).Returns(true);

            using (FileSystemHelpers.Override(fileSystem))
            {
                // Capture console output via ColoredConsole
                var output = new StringBuilder();
                var console = Substitute.For<IConsoleWriter>();
                console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
                console.Write(Arg.Do<object>(o => output.Append(o?.ToString()))).Returns(console);

                var oldOut = ColoredConsole.Out;
                var oldErr = ColoredConsole.Error;

                try
                {
                    ColoredConsole.Out = console;
                    ColoredConsole.Error = console;

                    // Act
                    await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), root, false);

                    // Assert
                    output.ToString().Should().Contain("Warning: Cannot find value named 'blah'");

                    // Match either slash or backslash in the path, regardless of OS
                    var escapedFolder2 = Regex.Escape(folder2);
                    var regex = new Regex($@"Warning: 'connection' property in '{escapedFolder2}[/\\]function\.json' is empty\.");
                    regex.IsMatch(output.ToString()).Should().BeTrue("Output should match the expected pattern");
                }
                finally
                {
                    ColoredConsole.Out = oldOut;
                    ColoredConsole.Error = oldErr;
                }
            }
        }

        private static IFileSystem GetFakeFileSystem(IEnumerable<(string Folder, string FunctionJsonContent)> list)
        {
            var fileSystem = Substitute.For<IFileSystem>();

            fileSystem.Directory.GetDirectories(Arg.Any<string>())
                .Returns(list.Select(t => t.Folder).ToArray());

            // Only function.json files exist where we expect them; other files can be true if your SUT probes
            fileSystem.File.Exists(Arg.Any<string>()).Returns(ci =>
            {
                var p = ci.Arg<string>();
                return list.Any(t => string.Equals(p, Path.Combine(t.Folder, "function.json"), StringComparison.OrdinalIgnoreCase));
            });

            foreach ((var folder, var fileContent) in list)
            {
                var functionJsonPath = Path.Combine(folder, "function.json");
                byte[] bytes = Encoding.UTF8.GetBytes(fileContent);

                // Fresh stream every call for this path
                fileSystem.File
                    .Open(Arg.Is(functionJsonPath), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                    .Returns(ci => new MemoryStream(bytes, writable: false));
            }

            return fileSystem;
        }

        [Theory]

        // In-proc target, in-proc 8 argument, project configured for .NET 8. Succeeds.
        [InlineData(WorkerRuntime.Dotnet, DotnetConstants.InProc8HostRuntime, true, false)]

        // In-proc target, in-proc 8 argument, project NOT configured for .NET 8. Fails.
        [InlineData(WorkerRuntime.Dotnet, DotnetConstants.InProc8HostRuntime, false, true)]

        // In-proc target, in-proc 6 argument, project NOT configured for .NET 8. Succeeds.
        [InlineData(WorkerRuntime.Dotnet, DotnetConstants.InProc6HostRuntime, false, false)]

        // In-proc target,'default' argument, project configured for .NET 8. Fails.
        [InlineData(WorkerRuntime.Dotnet, "default", true, true)]

        // Isolated target,'default' argument, project NOT configured for .NET 8. Succeeds.
        [InlineData(WorkerRuntime.DotnetIsolated, "default", true, false)]

        // Isolated target,'default' argument, project configured for .NET 8. Succeeds.
        [InlineData(WorkerRuntime.DotnetIsolated, "default", false, false)]

        // Isolated target,in-proc 8 argument, project configured for .NET 8. Fails.
        [InlineData(WorkerRuntime.DotnetIsolated, DotnetConstants.InProc8HostRuntime, true, true)]

        // Isolated target,in-proc 6 argument, project not configured for .NET 8. Fails.
        [InlineData(WorkerRuntime.DotnetIsolated, DotnetConstants.InProc6HostRuntime, false, true)]

        // Unsupported runtime targets.
        [InlineData(WorkerRuntime.DotnetIsolated, "somevalue", false, true)]
        [InlineData(WorkerRuntime.Dotnet, "somevalue", false, true)]

        // Non .NET worker runtimes.
        [InlineData(WorkerRuntime.Python, "default", false, false)]
        [InlineData(WorkerRuntime.Java, "default", false, false)]
        [InlineData(WorkerRuntime.Node, "default", false, false)]
        [InlineData(WorkerRuntime.Python, DotnetConstants.InProc6HostRuntime, false, true)]
        [InlineData(WorkerRuntime.Java, DotnetConstants.InProc6HostRuntime, false, true)]
        [InlineData(WorkerRuntime.Node, DotnetConstants.InProc6HostRuntime, false, true)]
        [InlineData(WorkerRuntime.Python, DotnetConstants.InProc8HostRuntime, false, true)]
        [InlineData(WorkerRuntime.Java, DotnetConstants.InProc8HostRuntime, false, true)]
        [InlineData(WorkerRuntime.Node, DotnetConstants.InProc8HostRuntime, false, true)]
        public async Task ValidateHostRuntimeAsync_MatchesExpectedResults(WorkerRuntime currentRuntime, string hostRuntimeArgument, bool validNet8Configuration, bool expectException)
        {
            try
            {
                Mock<IProcessManager> processManager = new();
                Mock<ISecretsManager> secretsManager = new();

                var startHostAction = new StartHostAction(secretsManager.Object, processManager.Object)
                {
                    HostRuntime = hostRuntimeArgument
                };

                await startHostAction.ValidateHostRuntimeAsync(currentRuntime, () => Task.FromResult(validNet8Configuration));
            }
            catch (CliException)
            {
                if (!expectException)
                {
                    throw;
                }

                return;
            }

            Assert.False(expectException, "Expected validation failure.");
        }

        [Fact]
        public async Task GetConfigurationSettings_OverwritesAzFuncEnvironment_WhenAlreadyInSecrets()
        {
            // Arrange
            var secretsDict = new Dictionary<string, string>
            {
                ["AZURE_FUNCTIONS_ENVIRONMENT"] = "UserEnv"
            };

            var mockSecretsManager = new Mock<ISecretsManager>();
            mockSecretsManager.Setup(s => s.GetSecrets(false))
                              .Returns(() => new Dictionary<string, string>(secretsDict));

            // Return an empty set of connection strings of the expected type
            mockSecretsManager.Setup(s => s.GetConnectionStrings())
                              .Returns(Array.Empty<ConnectionString>);

            // Set up file system mock to avoid the project root directory error
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);
            fileSystem.Directory.GetDirectories(Arg.Any<string>()).Returns(Array.Empty<string>());

            GlobalCoreToolsSettings.Init(mockSecretsManager.Object, []);

            using (FileSystemHelpers.Override(fileSystem))
            {
                var action = new StartHostAction(mockSecretsManager.Object, Mock.Of<IProcessManager>())
                {
                    DotNetIsolatedDebug = false,
                    EnableJsonOutput = false,
                    VerboseLogging = false,
                    HostRuntime = "default"
                };

                // Act
                var result = await action.GetConfigurationSettings("some/path", new Uri("https://example.com"));

                // Assert
                Assert.Equal("Development", result["AZURE_FUNCTIONS_ENVIRONMENT"]);
            }
        }

        [Fact]
        public void ConfigureHostLoggingForUserLogLevel_SetsEnvironmentVariable_WhenNeitherEnvVarNorHostJsonAreSet()
        {
            // Arrange
            var hostJsonConfig = TestUtilities.CreateSetupWithConfiguration(new Dictionary<string, string>());
            var loggingFilterHelper = new LoggingFilterHelper(hostJsonConfig, false, "Debug");
            
            // Clear environment variable if it exists
            string envVarName = Constants.FunctionLogLevel;
            string originalValue = Environment.GetEnvironmentVariable(envVarName);
            Environment.SetEnvironmentVariable(envVarName, null);

            try
            {
                // Act
                StartHostAction.ConfigureHostLoggingForUserLogLevel(loggingFilterHelper, hostJsonConfig);

                // Assert
                string actualValue = Environment.GetEnvironmentVariable(envVarName);
                Assert.Equal("Debug", actualValue);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(envVarName, originalValue);
            }
        }

        [Fact]
        public void ConfigureHostLoggingForUserLogLevel_DoesNotSetEnvironmentVariable_WhenEnvVarAlreadySet()
        {
            // Arrange
            var hostJsonConfig = TestUtilities.CreateSetupWithConfiguration(new Dictionary<string, string>());
            var loggingFilterHelper = new LoggingFilterHelper(hostJsonConfig, false, "Debug");
            
            string envVarName = Constants.FunctionLogLevel;
            string originalValue = Environment.GetEnvironmentVariable(envVarName);
            Environment.SetEnvironmentVariable(envVarName, "Warning");

            try
            {
                // Act
                StartHostAction.ConfigureHostLoggingForUserLogLevel(loggingFilterHelper, hostJsonConfig);

                // Assert - should preserve existing env var value
                string actualValue = Environment.GetEnvironmentVariable(envVarName);
                Assert.Equal("Warning", actualValue);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(envVarName, originalValue);
            }
        }

        [Fact]
        public void ConfigureHostLoggingForUserLogLevel_DoesNotSetEnvironmentVariable_WhenHostJsonHasFunctionLogLevel()
        {
            // Arrange
            var hostJsonSettings = new Dictionary<string, string>
            {
                ["logging:logLevel:Function"] = "Error"
            };
            var hostJsonConfig = TestUtilities.CreateSetupWithConfiguration(hostJsonSettings);
            var loggingFilterHelper = new LoggingFilterHelper(hostJsonConfig, false, "Debug");
            
            string envVarName = Constants.FunctionLogLevel;
            string originalValue = Environment.GetEnvironmentVariable(envVarName);
            Environment.SetEnvironmentVariable(envVarName, null);

            try
            {
                // Act
                StartHostAction.ConfigureHostLoggingForUserLogLevel(loggingFilterHelper, hostJsonConfig);

                // Assert - should not set env var since host.json has the setting
                string actualValue = Environment.GetEnvironmentVariable(envVarName);
                Assert.Null(actualValue);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(envVarName, originalValue);
            }
        }

        [Fact]
        public void ConfigureHostLoggingForUserLogLevel_AlwaysSetsPythonDebugLogging()
        {
            // Arrange
            var hostJsonConfig = TestUtilities.CreateSetupWithConfiguration(new Dictionary<string, string>());
            var loggingFilterHelper = new LoggingFilterHelper(hostJsonConfig, false, "Information");
            
            string pythonEnvVar = "PYTHON_ENABLE_DEBUG_LOGGING";
            string originalValue = Environment.GetEnvironmentVariable(pythonEnvVar);
            Environment.SetEnvironmentVariable(pythonEnvVar, null);

            try
            {
                // Act
                StartHostAction.ConfigureHostLoggingForUserLogLevel(loggingFilterHelper, hostJsonConfig);

                // Assert
                string actualValue = Environment.GetEnvironmentVariable(pythonEnvVar);
                Assert.Equal("1", actualValue);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(pythonEnvVar, originalValue);
            }
        }

        [Fact]
        public void ConfigureHostLoggingForUserLogLevel_DoesNotOverridePythonDebugLogging_WhenAlreadySet()
        {
            // Arrange
            var hostJsonConfig = TestUtilities.CreateSetupWithConfiguration(new Dictionary<string, string>());
            var loggingFilterHelper = new LoggingFilterHelper(hostJsonConfig, false, "Information");
            
            string pythonEnvVar = "PYTHON_ENABLE_DEBUG_LOGGING";
            string originalValue = Environment.GetEnvironmentVariable(pythonEnvVar);
            Environment.SetEnvironmentVariable(pythonEnvVar, "0");

            try
            {
                // Act
                StartHostAction.ConfigureHostLoggingForUserLogLevel(loggingFilterHelper, hostJsonConfig);

                // Assert - should preserve existing value
                string actualValue = Environment.GetEnvironmentVariable(pythonEnvVar);
                Assert.Equal("0", actualValue);
            }
            finally
            {
                // Cleanup
                Environment.SetEnvironmentVariable(pythonEnvVar, originalValue);
            }
        }
    }
}
