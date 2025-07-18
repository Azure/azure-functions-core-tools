// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Abstractions;
using System.Runtime.InteropServices;
using System.Text;

using Azure.Functions.Cli.Actions.HostActions;
using Azure.Functions.Cli.Common;
using Azure.Functions.Cli.Helpers;
using Azure.Functions.Cli.Interfaces;

using Colors.Net;

using FluentAssertions;
using FluentAssertions.Execution;

using Moq;

using NSubstitute;

using Xunit;

namespace Azure.Functions.Cli.UnitTests.ActionsTests
{
    public class StartHostActionTests : IDisposable
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

            FileSystemHelpers.Instance = fileSystem;

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
            exception.Message.Should().Contain($"Missing value for AzureWebJobsStorage in local.settings.json. " +
                $"This is required for all triggers other than {string.Join(", ", Common.Constants.TriggersWithoutStorage)}.");
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

            FileSystemHelpers.Instance = fileSystem;

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

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage:blobServiceUri", "myuri" },
                { "AzureWebJobsStorage__queueServiceUri", "queueuri" }
            };

            FileSystemHelpers.Instance = fileSystem;

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

            var secrets = new Dictionary<string, string>()
            {
                { "AzureWebJobsStorage:blobServiceUri", "myuri" },
                { "AzureWebJobsStorage__queueServiceUri", "queueuri" }
            };

            FileSystemHelpers.Instance = fileSystem;

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

        [SkippableFact]
        public async Task CheckNonOptionalSettingsPrintsWarningForMissingSettings()
        {
            Skip.IfNot(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                reason: "Environment.CurrentDirectory throws in linux in test cases for some reason. Revisit this once we figure out why it's failing");

            var fileSystem = GetFakeFileSystem(
            [
                ("x:\\folder1", "{'bindings': [{'type': 'httpTrigger', 'connection': 'blah'}]}"),
                ("x:\\folder2", "{'bindings': [{'type': 'httpTrigger', 'connection': ''}]}")
            ]);

            FileSystemHelpers.Instance = fileSystem;

            var output = new StringBuilder();
            var console = Substitute.For<IConsoleWriter>();
            console.WriteLine(Arg.Do<object>(o => output.AppendLine(o?.ToString()))).Returns(console);
            console.Write(Arg.Do<object>(o => output.Append(o.ToString()))).Returns(console);
            ColoredConsole.Out = console;
            ColoredConsole.Error = console;

            await StartHostAction.CheckNonOptionalSettings(new Dictionary<string, string>(), "x:\\", false);
            output.ToString().Should().Contain("Warning: Cannot find value named 'blah'");
            output.ToString().Should().Contain("Warning: 'connection' property in 'x:\\folder2\\function.json' is empty.");
        }

        private IFileSystem GetFakeFileSystem(IEnumerable<(string Folder, string FunctionJsonContent)> list)
        {
            var fileSystem = Substitute.For<IFileSystem>();
            fileSystem.Directory.GetDirectories(Arg.Any<string>()).Returns(list.Select(t => t.Folder).ToArray());
            fileSystem.File.Exists(Arg.Any<string>()).Returns(true);

            foreach ((var folder, var fileContent) in list)
            {
                fileSystem.File.Open(Arg.Is(Path.Combine(folder, "function.json")), Arg.Any<FileMode>(), Arg.Any<FileAccess>(), Arg.Any<FileShare>())
                    .Returns(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(fileContent)));
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

        [InlineData(WorkerRuntime.DotnetIsolated)]
        [InlineData(WorkerRuntime.Python)]
        [InlineData(WorkerRuntime.Java)]
        [InlineData(WorkerRuntime.Node)]
        [Theory]
        public async Task AzureFunctionsEnvironment_EnvironmentVariable_SetByUser_DoesNotThrow(WorkerRuntime currentRuntime)
        {
            Environment.SetEnvironmentVariable(Common.Constants.FunctionsWorkerRuntime, WorkerRuntimeLanguageHelper.GetRuntimeMoniker(currentRuntime));
            GlobalCoreToolsSettings.SetWorkerRuntime(currentRuntime);

            Mock<IProcessManager> processManager = new();
            Mock<ISecretsManager> secretsManager = new();

            var settings = new Dictionary<string, string> { ["AZURE_FUNCTIONS_ENVIRONMENT"] = "MyEnvironment" };
            secretsManager.Setup(s => s.GetSecrets()).Returns(() => settings);

            GlobalCoreToolsSettings.Init(secretsManager.Object, []);

            var startHostAction = new StartHostAction(secretsManager.Object, processManager.Object)
            {
                HostRuntime = "default",
                VerboseLogging = true,
            };

            try
            {
                await startHostAction.RunAsync().WaitAsync(TimeSpan.FromSeconds(1));
                throw new AssertionFailedException("Should've canceled via timeout");
            }
            catch (TimeoutException)
            {
            }

            Assert.Equal("MyEnvironment", settings["AZURE_FUNCTIONS_ENVIRONMENT"]);
        }

        public void Dispose()
        {
            FileSystemHelpers.Instance = null;
        }
    }
}
